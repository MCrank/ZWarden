using ZWarden.Domain.Ids;

namespace ZWarden.Application.Players;

/// <summary>
/// The in-memory, latest-roster-per-Server store (F19), mirroring F16's metrics/health caches. A player roster
/// is transient display data — the newest one per Server lives here and feeds the live UI island, never the
/// database. Process-local and a singleton. Reads are <b>ownership-guarded</b>: a roster is returned only to a
/// caller that names the Server's true owning Agent, so a roster reported for an unguessable ServerId cannot
/// surface under the wrong Server (trust-boundaries.md §8).
/// </summary>
public interface IPlayerRosterCache
{
    /// <summary>Records the latest roster a Server reported (last write per Server wins).</summary>
    void Record(PlayerRoster roster);

    /// <summary>The latest roster for a Server, but only if it was reported by <paramref name="owningAgentId"/>
    /// (the Server's true owner); otherwise <c>null</c>.</summary>
    PlayerRoster? GetLatest(ServerId serverId, AgentId owningAgentId);
}
