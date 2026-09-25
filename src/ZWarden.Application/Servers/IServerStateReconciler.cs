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

    /// <summary>
    /// Records the installed Steam build id a successful SteamCMD update Operation reported (F17), against the
    /// Server the completion named. Observed, tenant-scoped — a report for a Server not in the current tenant is
    /// a no-op (trust-boundaries.md §3/§8). A <c>null</c> build id still stamps the reported time.
    /// </summary>
    Task RecordInstalledBuildAsync(
        ServerId serverId,
        string? buildId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the Steam build id an Agent read from a Server's install manifest and reported in its metrics
    /// (#257), so a server installed by its first boot shows a Version without a manual Update. Tenant-scoped and
    /// ownership-guarded like <see cref="RecordObservedStateAsync"/>. An unchanged build is not re-written (the
    /// report time keeps meaning "when this build was first seen"), and a value over the stored bound is ignored.
    /// </summary>
    Task RecordReportedBuildAsync(
        AgentId agentId,
        ServerId serverId,
        string buildId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the game version (e.g. <c>42.20.4</c>) an Agent read from a Server's boot log and reported in its
    /// metrics (#262). Persisted rather than cache-only because Docker log rotation can drop the boot line on a
    /// long-running container. Ownership-guarded and bounded like <see cref="RecordReportedBuildAsync"/>; an
    /// unchanged value is not re-written.
    /// </summary>
    Task RecordReportedGameVersionAsync(
        AgentId agentId,
        ServerId serverId,
        string gameVersion,
        CancellationToken cancellationToken = default);
}
