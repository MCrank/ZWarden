using ZWarden.Application.Servers;
using ZWarden.Domain.Ids;

namespace ZWarden.Web.Components.Servers;

/// <summary>
/// The Fleet's Hosts at a glance (#357): how many there are, how many are connected, and the names of the unreachable
/// ones — the Hosts tile and the degraded banner. The Hosts are the distinct owning Agents of the visible Servers plus
/// <paramref name="knownHosts"/> (every Host, for a caller who may view Hosts, #342), so it needs no <c>Agent.View</c>
/// and never widens what the caller sees (ADR 0018). The page's first render and <c>/api/fleet/hosts</c> (polled by
/// live-status.js) both build it here, so they agree. Names are untrusted, observed strings (rendered as data).
/// </summary>
public static class FleetHosts
{
    public static FleetHostsSummary Summarize(
        IEnumerable<ServerSummary> servers,
        IEnumerable<AgentId> knownHosts,
        Func<AgentId, string> nameOf,
        Func<AgentId, bool> isConnected)
    {
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(knownHosts);
        ArgumentNullException.ThrowIfNull(nameOf);
        ArgumentNullException.ThrowIfNull(isConnected);

        List<AgentId> hosts = servers.Select(s => s.AgentId).Concat(knownHosts).Distinct().ToList();
        List<string> unreachable = hosts.Where(a => !isConnected(a)).Select(nameOf).ToList();
        return new FleetHostsSummary(hosts.Count, hosts.Count - unreachable.Count, unreachable);
    }
}

/// <summary>The Fleet's Hosts (#357): <paramref name="Total"/>, <paramref name="Online"/> and the names of those whose
/// Agent is not connected, in fleet order.</summary>
public sealed record FleetHostsSummary(int Total, int Online, IReadOnlyList<string> Unreachable);
