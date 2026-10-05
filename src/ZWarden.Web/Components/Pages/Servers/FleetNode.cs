using ZWarden.Domain.Ids;
using ZWarden.Web.Components.Hosts;
using ZWarden.Web.Components.Servers;

namespace ZWarden.Web.Components.Pages.Servers;

/// <summary>
/// One row of the hierarchical Fleet grid (#340): a Host rollup row (<see cref="Host"/>, with its Servers as
/// <see cref="Children"/>) or a Server row (<see cref="Server"/>). BbDataGrid's hierarchy takes a single row type, so
/// both kinds share this record. Public and serializable: it crosses the prerender→interactive boundary as the
/// <see cref="FleetBoard"/> island's parameter.
/// </summary>
/// <param name="Key">The grid's unique node value: the Host's AgentId (its short id for a caller who may not view Hosts),
/// <see cref="FleetTree.UnassignedKey"/>, or the Server's id (the <c>agt-</c> / <c>srv-</c> prefixes keep them apart).
/// Also the collapse-state key.</param>
/// <param name="Name">The Host's or Server's name — the hierarchy column's value and the hosts' sort key.</param>
/// <param name="Server">The Server row, or <c>null</c> for a Host row.</param>
/// <param name="Host">The Host rollup, or <c>null</c> for a Server row.</param>
/// <param name="Children">A Host row's Servers; <c>null</c> for a Server row.</param>
public sealed record FleetNode(
    string Key,
    string Name,
    FleetRow? Server = null,
    FleetHostRow? Host = null,
    List<FleetNode>? Children = null);

/// <summary>A Host rollup row's facts (#340), computed once at load; live-status.js keeps the players and meters current.</summary>
/// <param name="AgentId">The Host's AgentId (the live telemetry key and the /hosts anchor), or <c>null</c> for the
/// Unassigned group and for a caller who may not view Hosts.</param>
/// <param name="Connected">The Agent is connected now (the Online / Unreachable chip).</param>
/// <param name="Unassigned">The group of Servers whose Agent was deleted or is unknown.</param>
/// <param name="ServerCount">How many Servers the row groups.</param>
/// <param name="MemberIds">Its Servers' ids, space-separated, so the poll can re-sum players while the row is collapsed.</param>
/// <param name="PlayersText">Summed players over summed caps (e.g. <c>18 / 40</c>), as <see cref="FleetFacts.FormatPlayers"/>.</param>
/// <param name="PlayersKnown">At least one Server has a player sample.</param>
/// <param name="Telemetry">The row shows live Host CPU / Memory meters (the caller may view Hosts and it is connected).</param>
/// <param name="CpuPercent">Host-wide CPU busy % at load.</param>
/// <param name="MemoryUsedBytes">Host RAM in use at load.</param>
/// <param name="MemoryTotalBytes">Host RAM in total at load.</param>
public sealed record FleetHostRow(
    string? AgentId,
    bool Connected,
    bool Unassigned,
    int ServerCount,
    string MemberIds,
    string PlayersText,
    bool PlayersKnown,
    bool Telemetry,
    double? CpuPercent,
    long? MemoryUsedBytes,
    long? MemoryTotalBytes);

/// <summary>What the Fleet page knows about one Host (#340): its operator-facing name (#336), whether it is connected,
/// and its telemetry when the caller may view Hosts (else <c>null</c>). <paramref name="Identified"/>: the caller may view
/// Hosts, so the row is keyed by — and carries — the AgentId; anyone else gets the Host's short id (its name) as the key,
/// so the page never shows them the full id (#336 D3).</summary>
public sealed record FleetHostInfo(string Name, bool Connected, HostTelemetry? Telemetry, bool Identified = true);

/// <summary>Builds the Fleet grid's Host → Server hierarchy (#340).</summary>
public static class FleetTree
{
    /// <summary>The node value of the group of Servers on a deleted or unknown Agent.</summary>
    public const string UnassignedKey = "unassigned";

    /// <summary>
    /// Groups the projected Server rows under their Host, hosts by name and the "Unassigned" group last.
    /// <paramref name="hostOf"/> returns <c>null</c> for an Agent that is deleted or unknown to the caller.
    /// Servers sort by name within each Host. Each of <paramref name="knownHosts"/> with no Server still gets an empty
    /// row (a freshly enrolled Host belongs on the Fleet before anything is deployed to it).
    /// </summary>
    public static List<FleetNode> Build(
        IEnumerable<(AgentId Agent, FleetRow Row)> servers, Func<AgentId, FleetHostInfo?> hostOf,
        IEnumerable<AgentId>? knownHosts = null)
    {
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(hostOf);

        List<FleetNode> hosts = [];
        List<FleetRow> unassigned = [];
        HashSet<AgentId> grouped = [];
        foreach (IGrouping<AgentId, FleetRow> group in servers.GroupBy(s => s.Agent, s => s.Row))
        {
            grouped.Add(group.Key);
            if (hostOf(group.Key) is { } info)
            {
                string? agentId = info.Identified ? group.Key.ToString() : null;
                hosts.Add(Group(agentId ?? info.Name, info.Name, [.. group], agentId, info));
            }
            else
            {
                unassigned.AddRange(group);
            }
        }

        foreach (AgentId idle in (knownHosts ?? []).Where(grouped.Add))
        {
            if (hostOf(idle) is { } info)
            {
                string? agentId = info.Identified ? idle.ToString() : null;
                hosts.Add(Group(agentId ?? info.Name, info.Name, [], agentId, info));
            }
        }

        hosts.Sort((x, y) => StringComparer.OrdinalIgnoreCase.Compare(x.Name, y.Name));
        if (unassigned.Count > 0)
        {
            hosts.Add(Group(UnassignedKey, "Unassigned", unassigned, agentId: null, info: null));
        }

        return hosts;
    }

    private static FleetNode Group(string key, string name, List<FleetRow> group, string? agentId, FleetHostInfo? info)
    {
        List<FleetRow> rows = [.. group.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)];
        List<int> players = [.. rows.Where(r => r.Players is not null).Select(r => r.Players!.Value)];
        List<int> caps = [.. rows.Where(r => r.MaxPlayers is not null).Select(r => r.MaxPlayers!.Value)];
        HostTelemetry? telemetry = info?.Telemetry;
        FleetHostRow host = new(
            agentId,
            info?.Connected ?? false,
            info is null,
            rows.Count,
            string.Join(' ', rows.Select(r => r.Id)),
            FleetFacts.FormatPlayers(players.Count == 0 ? null : players.Sum(), caps.Count == 0 ? null : caps.Sum()),
            players.Count > 0,
            telemetry is not null,
            telemetry?.CpuPercent,
            telemetry?.MemoryUsedBytes,
            telemetry?.MemoryTotalBytes);
        return new FleetNode(key, name, Host: host, Children: [.. rows.Select(r => new FleetNode(r.Id, r.Name, Server: r))]);
    }
}
