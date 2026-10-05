using ZWarden.Application.Servers;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;
using ZWarden.Web.Components.Servers;

namespace ZWarden.Web.Tests.Servers;

/// <summary>#357: the Fleet's Hosts — the one projection behind the first render and <c>/api/fleet/hosts</c>.</summary>
public sealed class FleetHostsTests
{
    [Test]
    public async Task Hosts_are_the_visible_fleets_agents_plus_the_known_hosts_once_each()
    {
        AgentId up = AgentId.New();
        AgentId down = AgentId.New();
        AgentId idle = AgentId.New();

        FleetHostsSummary summary = FleetHosts.Summarize(
            [Server(up), Server(up), Server(down)], [up, idle], a => a == down ? "down" : a == idle ? "idle" : "up", a => a == up);

        await Assert.That(summary.Total).IsEqualTo(3);
        await Assert.That(summary.Online).IsEqualTo(1);
        await Assert.That(summary.Unreachable).IsEquivalentTo(["down", "idle"]);
    }

    [Test]
    public async Task An_empty_fleet_has_no_hosts()
    {
        FleetHostsSummary summary = FleetHosts.Summarize([], [], _ => "x", _ => false);

        await Assert.That(summary).IsEqualTo(new FleetHostsSummary(0, 0, summary.Unreachable));
        await Assert.That(summary.Unreachable).IsEmpty();
    }

    private static ServerSummary Server(AgentId agent) =>
        new(ServerId.New(), agent, "pz", null, null, null, ServerRunState.Running, null, null, null, null);
}
