using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;
using ZWarden.Web.Components.Pages.Servers;

namespace ZWarden.Web.Tests.Servers;

/// <summary>
/// #340: the Fleet grid's hierarchy — every Server under a Host rollup row, hosts by name, Servers on a deleted or
/// unknown Agent under one trailing "Unassigned" group, and each Host row summing its Servers' players (as FleetFacts
/// formats one Server's).
/// </summary>
public class FleetTreeTests
{
    private static FleetRow Row(string name, int? players = null, int? max = null) =>
        new(ServerId.New().ToString(), name, null, ServerRunState.Running, "RUNNING", ServerRunState.Running, "—",
            null, null, null, players, MaxPlayers: max);

    private static FleetHostInfo Online(string name) => new(name, Connected: true, Telemetry: null);

    [Test]
    public async Task Servers_group_under_their_host_and_hosts_sort_by_name()
    {
        AgentId zulu = AgentId.New(), alpha = AgentId.New();
        FleetRow a = Row("a"), b = Row("b"), c = Row("c");
        Dictionary<AgentId, FleetHostInfo> hosts = new() { [zulu] = Online("zulu"), [alpha] = Online("Alpha") };

        List<FleetNode> tree = FleetTree.Build([(zulu, b), (alpha, c), (zulu, a)], hosts.GetValueOrDefault);

        await Assert.That(tree.Select(n => n.Name)).IsEquivalentTo(["Alpha", "zulu"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(tree[0].Key).IsEqualTo(alpha.ToString());
        await Assert.That(tree[0].Host!.AgentId).IsEqualTo(alpha.ToString());
        await Assert.That(tree[1].Children!.Select(n => n.Server!.Name)).IsEquivalentTo(["a", "b"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(tree[1].Children![0].Key).IsEqualTo(a.Id);
        await Assert.That(tree[1].Host!.ServerCount).IsEqualTo(2);
        await Assert.That(tree[1].Host!.MemberIds).IsEqualTo($"{a.Id} {b.Id}");
    }

    [Test]
    public async Task A_known_host_with_no_servers_gets_an_empty_row_in_name_order()
    {
        AgentId busy = AgentId.New(), idle = AgentId.New();
        Dictionary<AgentId, FleetHostInfo> hosts = new() { [busy] = Online("zulu"), [idle] = Online("alpha") };

        List<FleetNode> tree = FleetTree.Build([(busy, Row("a"))], hosts.GetValueOrDefault, knownHosts: [busy, idle]);

        await Assert.That(tree.Select(n => n.Name)).IsEquivalentTo(["alpha", "zulu"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(tree[0].Key).IsEqualTo(idle.ToString());
        await Assert.That(tree[0].Host!.ServerCount).IsEqualTo(0);
        await Assert.That(tree[0].Host!.Unassigned).IsFalse();
        await Assert.That(tree[0].Children!).IsEmpty();
        await Assert.That(tree[1].Host!.ServerCount).IsEqualTo(1);
    }

    [Test]
    public async Task One_host_is_still_a_hierarchy()
    {
        AgentId agent = AgentId.New();

        List<FleetNode> tree = FleetTree.Build([(agent, Row("solo"))], _ => Online("nsfw-01"));

        await Assert.That(tree).Count().IsEqualTo(1);
        await Assert.That(tree[0].Host).IsNotNull();
        await Assert.That(tree[0].Children!).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Servers_on_an_unknown_agent_go_in_one_trailing_unassigned_group()
    {
        AgentId known = AgentId.New(), goneA = AgentId.New(), goneB = AgentId.New();

        List<FleetNode> tree = FleetTree.Build(
            [(goneA, Row("x")), (known, Row("k")), (goneB, Row("y"))],
            a => a == known ? Online("zzz") : null);

        await Assert.That(tree.Select(n => n.Name)).IsEquivalentTo(["zzz", "Unassigned"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        FleetHostRow unassigned = tree[1].Host!;
        await Assert.That(unassigned.Unassigned).IsTrue();
        await Assert.That(unassigned.AgentId).IsNull();
        await Assert.That(tree[1].Key).IsEqualTo(FleetTree.UnassignedKey);
        await Assert.That(tree[1].Children!.Select(n => n.Server!.Name)).IsEquivalentTo(["x", "y"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_host_row_sums_its_servers_players_over_their_caps()
    {
        AgentId agent = AgentId.New();

        List<FleetNode> tree = FleetTree.Build(
            [(agent, Row("a", players: 2, max: 16)), (agent, Row("b", players: 0, max: 24)), (agent, Row("c"))],
            _ => Online("h"));

        await Assert.That(tree[0].Host!.PlayersText).IsEqualTo("2 / 40");
        await Assert.That(tree[0].Host!.PlayersKnown).IsTrue();
    }

    [Test]
    public async Task A_host_with_no_player_samples_reads_dash()
    {
        AgentId agent = AgentId.New();

        List<FleetNode> tree = FleetTree.Build([(agent, Row("a"))], _ => Online("h"));

        await Assert.That(tree[0].Host!.PlayersText).IsEqualTo("—");
        await Assert.That(tree[0].Host!.PlayersKnown).IsFalse();
    }

    [Test]
    public async Task A_host_row_carries_its_connection_and_telemetry_meters()
    {
        AgentId agent = AgentId.New();
        Components.Hosts.HostTelemetry telemetry = new(37.5, 4_000, 8_000, null, null, "", "", "as of 5 s ago", false);

        List<FleetNode> tree = FleetTree.Build([(agent, Row("a"))], _ => new FleetHostInfo("h", Connected: true, telemetry));

        FleetHostRow host = tree[0].Host!;
        await Assert.That(host.Connected).IsTrue();
        await Assert.That(host.Telemetry).IsTrue();
        await Assert.That(host.CpuPercent).IsEqualTo(37.5);
        await Assert.That(host.MemoryUsedBytes).IsEqualTo(4_000L);
        await Assert.That(host.MemoryTotalBytes).IsEqualTo(8_000L);
    }

    [Test]
    public async Task A_host_the_caller_may_not_view_is_keyed_by_its_name_and_carries_no_agent_id()
    {
        AgentId agent = AgentId.New();

        List<FleetNode> tree = FleetTree.Build([(agent, Row("a"))], _ => new FleetHostInfo("agt-short", true, null, Identified: false));

        await Assert.That(tree[0].Key).IsEqualTo("agt-short");
        await Assert.That(tree[0].Host!.AgentId).IsNull();
    }

    [Test]
    public async Task An_unreachable_host_has_no_telemetry()
    {
        AgentId agent = AgentId.New();

        List<FleetNode> tree = FleetTree.Build([(agent, Row("a"))], _ => new FleetHostInfo("h", Connected: false, null));

        await Assert.That(tree[0].Host!.Connected).IsFalse();
        await Assert.That(tree[0].Host!.Telemetry).IsFalse();
    }
}
