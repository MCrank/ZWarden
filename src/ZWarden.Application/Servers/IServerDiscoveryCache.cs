using ZWarden.Domain.Ids;

namespace ZWarden.Application.Servers;

/// <summary>
/// The in-memory, per-Agent record of the canonical containers last seen in that Agent's state snapshot
/// (F14). Like the F10 connection registry it is process-local and authoritative for "as of the last
/// snapshot"; it is not persisted (a Server that matters is a persisted <c>Server</c> record, not a cache
/// entry). It is the source the import picker reads to offer discovered-but-unregistered containers. The
/// entries are Agent-reported and untrusted (trust-boundaries.md §3).
/// </summary>
public interface IServerDiscoveryCache
{
    /// <summary>Replaces the discovered set for an Agent with its latest snapshot.</summary>
    void Record(AgentId agentId, IReadOnlyList<DiscoveredServer> servers);

    /// <summary>The Agent's last-reported discovered containers, or empty if it has reported none.</summary>
    IReadOnlyList<DiscoveredServer> GetDiscovered(AgentId agentId);

    /// <summary>Every Agent that has reported at least one discovery snapshot this process.</summary>
    IReadOnlyCollection<AgentId> KnownAgents();
}
