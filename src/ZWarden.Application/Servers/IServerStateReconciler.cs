using ZWarden.Domain.Ids;

namespace ZWarden.Application.Servers;

/// <summary>
/// Reconciles an Agent's observed state snapshot against the persisted Servers (F14; trust-boundaries.md
/// §3 — observed, never inferred). For each observed container that matches a persisted Server in the
/// ambient tenant, it records the last-reported run-state; it records the full observed set in the
/// discovery cache so unregistered containers can be offered for import. It never creates, deletes, or
/// infers a Server: an id with no record is an orphan to surface, not a row to write.
/// </summary>
public interface IServerStateReconciler
{
    /// <summary>Applies the Agent's latest snapshot: update matching Servers' last-reported state and refresh
    /// the discovery cache.</summary>
    Task ReconcileAsync(
        AgentId agentId,
        IReadOnlyList<DiscoveredServer> observed,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the container linkage a successful provisioning Operation reported (F14 PR-B): the allocated
    /// ports and the created container id, against the Server the completion named. Observed, tenant-scoped —
    /// a report for a Server not in the current tenant is a no-op (trust-boundaries.md §3/§8).
    /// </summary>
    Task RecordProvisionedAsync(
        ServerId serverId,
        int gamePort,
        int queryPort,
        string containerId,
        CancellationToken cancellationToken = default);
}
