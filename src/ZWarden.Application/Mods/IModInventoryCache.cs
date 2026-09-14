using ZWarden.Domain.Ids;

namespace ZWarden.Application.Mods;

/// <summary>
/// The in-memory, latest-inventory-per-Server store (F21), mirroring F19's roster cache and F16's metrics/health
/// caches. A mod inventory is transient display data — the newest one per Server lives here and feeds the live UI,
/// never the database. Process-local and a singleton. Reads are <b>ownership-guarded</b>: an inventory is returned
/// only to a caller that names the Server's true owning Agent, so an inventory reported for an unguessable ServerId
/// cannot surface under the wrong Server (trust-boundaries.md §8).
/// </summary>
public interface IModInventoryCache
{
    /// <summary>Records the latest inventory a Server reported (last write per Server wins).</summary>
    void Record(ModInventory inventory);

    /// <summary>The latest inventory for a Server, but only if it was reported by <paramref name="owningAgentId"/>
    /// (the Server's true owner); otherwise <c>null</c>.</summary>
    ModInventory? GetLatest(ServerId serverId, AgentId owningAgentId);
}
