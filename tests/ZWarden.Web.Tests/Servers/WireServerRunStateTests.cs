using ZWarden.Web.Servers;
using DomainRunState = ZWarden.Domain.Servers.ServerRunState;
using WireRunState = ZWarden.Contracts.Protocol.ServerRunState;

namespace ZWarden.Web.Tests.Servers;

/// <summary>
/// F14 (D4): the wire <see cref="WireRunState"/> maps onto the Domain's own <see cref="DomainRunState"/>.
/// The map is total — every wire value maps — so adding a wire state without teaching the Domain about it
/// fails here rather than silently degrading to Unknown.
/// </summary>
public class WireServerRunStateTests
{
    [Test]
    [Arguments(WireRunState.Unknown, DomainRunState.Unknown)]
    [Arguments(WireRunState.Stopped, DomainRunState.Stopped)]
    [Arguments(WireRunState.Starting, DomainRunState.Starting)]
    [Arguments(WireRunState.Running, DomainRunState.Running)]
    [Arguments(WireRunState.Stopping, DomainRunState.Stopping)]
    [Arguments(WireRunState.Failed, DomainRunState.Failed)]
    public async Task Each_wire_value_maps_to_its_domain_state(WireRunState wire, DomainRunState expected)
        => await Assert.That(WireServerRunState.ToDomain(wire)).IsEqualTo(expected);

    [Test]
    public async Task Every_wire_value_is_mapped()
    {
        WireRunState[] all = Enum.GetValues<WireRunState>();

        // Throws if a value is unmapped — the guard against silent drift (D4).
        List<DomainRunState> mapped = [.. all.Select(WireServerRunState.ToDomain)];

        await Assert.That(mapped.Count).IsEqualTo(all.Length);
    }
}
