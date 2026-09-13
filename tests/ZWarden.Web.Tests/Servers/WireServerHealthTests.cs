using ZWarden.Web.Servers;
using DomainHealth = ZWarden.Domain.Servers.ServerHealth;
using WireHealth = ZWarden.Contracts.Protocol.ServerHealth;

namespace ZWarden.Web.Tests.Servers;

/// <summary>
/// F16: the wire <see cref="WireHealth"/> maps onto the Domain's own <see cref="DomainHealth"/>, the health
/// sibling of <see cref="WireServerRunState"/>. The map is total — every wire value maps — so adding a wire
/// health state without teaching the Domain about it fails here rather than silently drifting.
/// </summary>
public class WireServerHealthTests
{
    [Test]
    [Arguments(WireHealth.Stopped, DomainHealth.Stopped)]
    [Arguments(WireHealth.Starting, DomainHealth.Starting)]
    [Arguments(WireHealth.Healthy, DomainHealth.Healthy)]
    [Arguments(WireHealth.Degraded, DomainHealth.Degraded)]
    [Arguments(WireHealth.Failed, DomainHealth.Failed)]
    public async Task Each_wire_value_maps_to_its_domain_health(WireHealth wire, DomainHealth expected)
        => await Assert.That(WireServerHealth.ToDomain(wire)).IsEqualTo(expected);

    [Test]
    public async Task Every_wire_value_is_mapped()
    {
        WireHealth[] all = Enum.GetValues<WireHealth>();

        List<DomainHealth> mapped = [.. all.Select(WireServerHealth.ToDomain)];

        await Assert.That(mapped.Count).IsEqualTo(all.Length);
    }
}
