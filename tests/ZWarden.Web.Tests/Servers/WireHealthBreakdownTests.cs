using ZWarden.Application.Servers;
using ZWarden.Web.Servers;
using WireBreakdown = ZWarden.Contracts.Protocol.Messages.HealthBreakdown;
using WireProbeCheck = ZWarden.Contracts.Protocol.Messages.ProbeCheck;
using WireProbeStatus = ZWarden.Contracts.Protocol.ProbeStatus;
using DomainProbeStatus = ZWarden.Domain.Servers.ProbeStatus;

namespace ZWarden.Web.Tests.Servers;

/// <summary>
/// F16 / #93: the wire <see cref="WireBreakdown"/> maps onto the transient live-cache
/// <see cref="LiveHealthBreakdown"/>, the probe-level sibling of <see cref="WireServerHealth"/>. The status map is
/// total — every wire value maps — so adding a wire probe status without teaching the live cache about it fails
/// here rather than silently drifting. Untrusted detail strings cross verbatim.
/// </summary>
public class WireHealthBreakdownTests
{
    [Test]
    [Arguments(WireProbeStatus.Pass, DomainProbeStatus.Pass)]
    [Arguments(WireProbeStatus.Warn, DomainProbeStatus.Warn)]
    [Arguments(WireProbeStatus.Fail, DomainProbeStatus.Fail)]
    [Arguments(WireProbeStatus.Skipped, DomainProbeStatus.Skipped)]
    public async Task Each_wire_probe_status_maps_to_its_domain_status(WireProbeStatus wire, DomainProbeStatus expected)
        => await Assert.That(WireHealthBreakdown.ToDomain(wire)).IsEqualTo(expected);

    [Test]
    public async Task Every_wire_probe_status_is_mapped()
    {
        WireProbeStatus[] all = Enum.GetValues<WireProbeStatus>();

        List<DomainProbeStatus> mapped = [.. all.Select(WireHealthBreakdown.ToDomain)];

        await Assert.That(mapped.Count).IsEqualTo(all.Length);
    }

    [Test]
    public async Task ToLive_carries_each_probe_and_its_untrusted_detail_verbatim()
    {
        WireBreakdown wire = new(
            new WireProbeCheck(WireProbeStatus.Pass),
            new WireProbeCheck(WireProbeStatus.Warn, "restarted twice in 5m"),
            new WireProbeCheck(WireProbeStatus.Skipped),
            new WireProbeCheck(WireProbeStatus.Fail, "query port 16261/udp unreachable"));

        LiveHealthBreakdown live = WireHealthBreakdown.ToLive(wire);

        await Assert.That(live.Container.Status).IsEqualTo(DomainProbeStatus.Pass);
        await Assert.That(live.Container.Detail).IsNull();
        await Assert.That(live.Process.Status).IsEqualTo(DomainProbeStatus.Warn);
        await Assert.That(live.Process.Detail).IsEqualTo("restarted twice in 5m");
        await Assert.That(live.Startup.Status).IsEqualTo(DomainProbeStatus.Skipped);
        await Assert.That(live.Network.Status).IsEqualTo(DomainProbeStatus.Fail);
        await Assert.That(live.Network.Detail).IsEqualTo("query port 16261/udp unreachable");
    }
}
