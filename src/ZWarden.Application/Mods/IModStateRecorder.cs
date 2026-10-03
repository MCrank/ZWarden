using ZWarden.Domain.Ids;

namespace ZWarden.Application.Mods;

/// <summary>
/// Persists what a mod discovery observed (#290, ADR 0047): the Server's configured <c>WorkshopItems=</c> /
/// <c>Mods=</c> (filling the booted-with snapshot when a boot is pending), and one <c>ServerWorkshopItem</c> per item
/// that is configured, booted with, or on disk, pruning the rest. Ownership-guarded: an inventory reported by an Agent
/// that doesn't own the Server is ignored (trust-boundaries §3). Mod ids from <c>mod.info</c> that fail
/// <c>PzModId</c> are dropped, never stored.
/// </summary>
public interface IModStateRecorder
{
    /// <summary>Records <paramref name="inventory"/> for its Server.</summary>
    Task RecordAsync(ModInventory inventory, CancellationToken cancellationToken = default);
}

/// <summary>
/// Turns Agent-hub events into background mod refreshes (#290 D1/D2). Called from the hub in the Agent's tenant scope;
/// it only marks state and queues work, so it never waits on an Agent or on Steam.
/// </summary>
public interface IModRefreshTrigger
{
    /// <summary>An operation succeeded. A boot (start, restart, update, recreate) marks the Server booted and queues
    /// a discovery now and a follow-up after the post-boot delay; a config apply queues a discovery. The kind and
    /// target are read from the persisted Operation, and only the owning Agent's report counts.</summary>
    Task OperationSucceededAsync(OperationId operationId, AgentId reportingAgent, CancellationToken cancellationToken = default);

    /// <summary>#291: a config apply is about to be marked succeeded. Its own <c>WorkshopItems=</c> / <c>Mods=</c>
    /// edits are written into the cached Mod Inventory first, so the next mod change (a second Install moments later)
    /// is computed from the lists just written, not the last discovery's. Called before completion, so no change can
    /// slip in between; the follow-up discovery then confirms the lists from disk. Only the owning Agent's report
    /// counts; it never throws into the hub.</summary>
    Task RecordAppliedModListsAsync(OperationId operationId, AgentId reportingAgent, CancellationToken cancellationToken = default);

    /// <summary>An Agent connected: queue a discovery for each Server it owns, so mod data comes back after a web
    /// restart without a boot.</summary>
    void AgentConnected(AgentId agent);
}
