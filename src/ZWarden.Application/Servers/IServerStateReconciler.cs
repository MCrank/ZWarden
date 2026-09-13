using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;

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
    /// <summary>Applies the Agent's latest snapshot: update matching Servers' last-reported run-state (and health,
    /// when the snapshot carries it) and refresh the discovery cache.</summary>
    Task ReconcileAsync(
        AgentId agentId,
        IReadOnlyList<DiscoveredServer> observed,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a single observed <b>run-state</b> transition the Agent reported (F16 <c>ServerStateChanged</c>) —
    /// the incremental companion to a full snapshot. Tenant-scoped and ownership-guarded: a report for a Server
    /// not in the current tenant, or not owned by <paramref name="agentId"/>, is a no-op (trust-boundaries.md §3/§8).
    /// </summary>
    Task RecordObservedStateAsync(
        AgentId agentId,
        ServerId serverId,
        ServerRunState runState,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a single observed <b>health</b> transition the Agent reported (F16 <c>HealthChanged</c>). Tenant-scoped
    /// and ownership-guarded exactly as <see cref="RecordObservedStateAsync"/>. Health is observed telemetry, not an
    /// audit event.
    /// </summary>
    Task RecordObservedHealthAsync(
        AgentId agentId,
        ServerId serverId,
        ServerHealth health,
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
