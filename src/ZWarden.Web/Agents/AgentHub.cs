using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using ZWarden.Application.Agents;
using ZWarden.Application.Audit;
using ZWarden.Application.Operations;
using ZWarden.Application.Servers;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Agents;
using ZWarden.Web.Observability;
using ZWarden.Web.Servers;

namespace ZWarden.Web.Agents;

/// <summary>
/// The SignalR hub an Agent connects to outbound over WSS (F10; criterion 14 — no inbound host port). The
/// <see cref="AgentAuthenticationHandler.SchemeName"/> scheme has already authenticated the connection by the
/// time any method here runs, so the Agent id is trusted off the connection principal. On connect the Agent
/// is registered in the in-memory <see cref="IAgentConnectionRegistry"/> (the seam F11 dispatches through);
/// <see cref="Hello"/> negotiates the protocol version (ADR 0020) and aborts an incompatible peer; heartbeats
/// and the post-connect snapshot advance the persisted last-seen. Connection lifecycle is audited (F6) with
/// no credential in any record.
/// </summary>
[Authorize(AuthenticationSchemes = AgentAuthenticationHandler.SchemeName)]
public sealed partial class AgentHub : Hub
{
    private readonly IAgentConnectionRegistry _registry;
    private readonly IAgentConnectionStateWriter _state;
    private readonly IOperationStore _operations;
    private readonly IServerStateReconciler _servers;
    private readonly IServerMetricsCache _metrics;
    private readonly IServerHealthCache _healthCache;
    private readonly ControlPlaneMetrics _telemetry;
    private readonly IAuditWriter _audit;
    private readonly ILogger<AgentHub> _logger;

    public AgentHub(
        IAgentConnectionRegistry registry,
        IAgentConnectionStateWriter state,
        IOperationStore operations,
        IServerStateReconciler servers,
        IServerMetricsCache metrics,
        IServerHealthCache healthCache,
        ControlPlaneMetrics telemetry,
        IAuditWriter audit,
        ILogger<AgentHub> logger)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(healthCache);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(logger);
        _registry = registry;
        _state = state;
        _operations = operations;
        _servers = servers;
        _metrics = metrics;
        _healthCache = healthCache;
        _telemetry = telemetry;
        _audit = audit;
        _logger = logger;
    }

    /// <inheritdoc />
    public override async Task OnConnectedAsync()
    {
        if (!AgentClaims.TryGetAgentId(Context.User, out AgentId agentId))
        {
            // The scheme guarantees the claim; this is defensive — refuse a principal we cannot address.
            Context.Abort();
            return;
        }

        _registry.Register(agentId, Context.ConnectionId, Context.Abort);
        await _audit.WriteAsync(Entry(AgentConnectionAuditActions.Connected, agentId), CancellationToken.None)
            .ConfigureAwait(false);
        LogConnected(agentId);
        await base.OnConnectedAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _registry.Remove(Context.ConnectionId);
        if (AgentClaims.TryGetAgentId(Context.User, out AgentId agentId))
        {
            // The connection token is already cancelled here, so persist under None or the write is cancelled.
            await _state.MarkDisconnectedAsync(agentId, CancellationToken.None).ConfigureAwait(false);
            await _audit.WriteAsync(Entry(AgentConnectionAuditActions.Disconnected, agentId), CancellationToken.None)
                .ConfigureAwait(false);
            LogDisconnected(agentId);
        }

        await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
    }

    /// <summary>
    /// The Agent's opening call: negotiate the protocol version carried on its <see cref="AgentHello"/>
    /// envelope (ADR 0020). An incompatible peer is told the actionable reason and its connection is aborted;
    /// a compatible one has its negotiated version persisted.
    /// </summary>
    public async Task<ProtocolNegotiationResult> Hello(Envelope<AgentHello> hello)
    {
        ArgumentNullException.ThrowIfNull(hello);
        AgentClaims.TryGetAgentId(Context.User, out AgentId agentId);

        ProtocolNegotiationResult result =
            ProtocolCompatibility.Negotiate(hello.ProtocolVersion, ProtocolVersionRange.Supported);

        if (!result.IsCompatible)
        {
            await _audit.WriteAsync(
                new AuditEntry(
                    AgentConnectionAuditActions.ConnectionRejected,
                    AuditOutcome.Failed,
                    null,
                    null,
                    $"agent {agentId}; {result.RejectionReason}"),
                CancellationToken.None).ConfigureAwait(false);
            LogProtocolRejected(agentId, result.RejectionReason ?? "incompatible");
            // Abort after this call returns, so the Agent reliably receives the negotiation result (and its
            // actionable reason) before the connection is torn down — aborting synchronously would cancel the
            // in-flight invocation.
            ScheduleAbort();
            return result;
        }

        await _state.MarkConnectedAsync(agentId, hello.ProtocolVersion, Context.ConnectionAborted).ConfigureAwait(false);
        return result;
    }

    /// <summary>Periodic liveness from the Agent (PRD 40): advance the persisted last-seen.</summary>
    public async Task Heartbeat(Envelope<AgentHeartbeat> heartbeat)
    {
        ArgumentNullException.ThrowIfNull(heartbeat);
        if (AgentClaims.TryGetAgentId(Context.User, out AgentId agentId))
        {
            await _state.MarkHeartbeatAsync(agentId, Context.ConnectionAborted).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The Agent's post-(re)connect state report (F14). Advances the persisted last-seen and reconciles the
    /// observed run-state onto the persisted Servers — observed, never inferred (trust-boundaries.md §3). The
    /// wire run-state is mapped onto the Domain's own (D4); the observed set also refreshes the discovery
    /// cache so unregistered containers can be offered for import. The payload is untrusted: it updates only
    /// Servers this tenant owns and creates none.
    /// </summary>
    public async Task StateSnapshot(Envelope<AgentStateSnapshot> snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (AgentClaims.TryGetAgentId(Context.User, out AgentId agentId))
        {
            await _state.MarkHeartbeatAsync(agentId, Context.ConnectionAborted).ConfigureAwait(false);

            List<DiscoveredServer> observed = snapshot.Payload.Servers
                .Select(s => new DiscoveredServer(
                    s.ServerId,
                    WireServerRunState.ToDomain(s.RunState),
                    s.Health is { } health ? WireServerHealth.ToDomain(health) : null))
                .ToList();
            await _servers.ReconcileAsync(agentId, observed, Context.ConnectionAborted).ConfigureAwait(false);
            LogSnapshot(agentId, snapshot.Payload.Servers.Count);
        }
    }

    /// <summary>
    /// The Agent's incremental run-state transition (F16): record the observed run-state on the named Server —
    /// observed, never inferred (trust-boundaries.md §3). Tenant-scoped and ownership-guarded in the reconciler:
    /// a report for a Server this Agent does not own is a no-op (trust-boundaries.md §8).
    /// </summary>
    public async Task ServerStateChanged(Envelope<ServerStateChanged> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (AgentClaims.TryGetAgentId(Context.User, out AgentId agentId))
        {
            await _state.MarkHeartbeatAsync(agentId, Context.ConnectionAborted).ConfigureAwait(false);
            await _servers.RecordObservedStateAsync(
                agentId,
                change.Payload.ServerId,
                WireServerRunState.ToDomain(change.Payload.RunState),
                Context.ConnectionAborted).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The Agent's incremental health transition (F16): record the observed health rollup on the named Server.
    /// Health is observed telemetry, not an audit event; the reconciler applies it only to a Server this Agent
    /// owns. The reason/breakdown are untrusted (trust-boundaries.md §8) and are not persisted in PR-A.
    /// </summary>
    public async Task HealthChanged(Envelope<HealthChanged> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (AgentClaims.TryGetAgentId(Context.User, out AgentId agentId))
        {
            await _state.MarkHeartbeatAsync(agentId, Context.ConnectionAborted).ConfigureAwait(false);
            Domain.Servers.ServerHealth health = WireServerHealth.ToDomain(change.Payload.Health);
            await _servers.RecordObservedHealthAsync(
                agentId,
                change.Payload.ServerId,
                health,
                Context.ConnectionAborted).ConfigureAwait(false);
            // Also cache the live rollup + reason so the interactive detail panel can show it without a
            // tenant-scoped read. The reason is untrusted (trust-boundaries.md §8), stored as data.
            _healthCache.Record(new ServerLiveHealth(
                agentId, change.Payload.ServerId, health, change.Payload.Reason, change.Timestamp));
            _telemetry.RecordHealthTransition(change.Payload.Health);
        }
    }

    /// <summary>
    /// The Agent's periodic runtime-metrics report (F16): the latest CPU/memory/disk sample per Server. Metrics
    /// are transient — recorded in the in-memory <see cref="IServerMetricsCache"/> (latest-sample-only) and pushed
    /// to the live UI, never persisted or audited. Each sample is stamped with the reporting Agent so the cache
    /// can refuse a sample forged for a Server this Agent does not own (trust-boundaries.md §8).
    /// </summary>
    public Task MetricsReport(Envelope<ServerMetricsReport> report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (AgentClaims.TryGetAgentId(Context.User, out AgentId agentId))
        {
            List<Application.Servers.ServerMetrics> mapped = report.Payload.Samples
                .Select(s => new Application.Servers.ServerMetrics(
                    agentId,
                    s.ServerId,
                    s.CpuPercent,
                    s.MemoryUsedBytes,
                    s.MemoryLimitBytes,
                    s.DiskUsedBytes,
                    s.DiskCapacityBytes,
                    s.PlayerCount,
                    s.SampledAt))
                .ToList();
            _metrics.Record(mapped);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// The Agent's progress report for an in-flight operation (F11). The operation is the envelope's
    /// <see cref="Envelope{TPayload}.OperationId"/>; the ingest is idempotent and resolves the operation in
    /// the (default) tenant. <see cref="OperationProgress.StatusLine"/> is untrusted (trust-boundaries.md §3).
    /// </summary>
    public async Task OperationProgress(Envelope<OperationProgress> progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (progress.OperationId is { } operationId)
        {
            await _operations.ApplyProgressAsync(
                operationId,
                progress.Payload.PercentComplete,
                progress.Payload.StatusLine,
                Context.ConnectionAborted).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The Agent's terminal report for an operation (F11): success or failure, mapped onto the operation's
    /// state. Idempotent — a redelivered completion for an already-terminal operation is ignored.
    /// </summary>
    public async Task OperationCompleted(Envelope<OperationCompleted> completed)
    {
        ArgumentNullException.ThrowIfNull(completed);
        if (completed.OperationId is not { } operationId)
        {
            return;
        }

        if (completed.Payload.Outcome == OperationOutcome.Succeeded)
        {
            // A successful provisioning Operation carries the container facts the Agent observed; record the
            // Server's container linkage before marking the operation done (F14 PR-B). Observed, tenant-scoped.
            if (completed.Payload.Provision is { } provision && completed.ServerId is { } serverId)
            {
                await _servers.RecordProvisionedAsync(
                    serverId, provision.GamePort, provision.QueryPort, provision.ContainerId, Context.ConnectionAborted)
                    .ConfigureAwait(false);
            }

            // A successful update Operation carries the build id the Agent read from the manifest (F17); record
            // it against the Server before the operation is marked done. Observed, tenant-scoped.
            if (completed.Payload.Update is { } update && completed.ServerId is { } updatedServerId)
            {
                await _servers.RecordInstalledBuildAsync(updatedServerId, update.InstalledBuildId, Context.ConnectionAborted)
                    .ConfigureAwait(false);
            }

            await _operations.CompleteSucceededAsync(operationId, Context.ConnectionAborted).ConfigureAwait(false);
        }
        else
        {
            await _operations.CompleteFailedAsync(operationId, completed.Payload.FailureReason, Context.ConnectionAborted)
                .ConfigureAwait(false);
        }
    }

    private static AuditEntry Entry(string action, AgentId agentId)
        => new(action, AuditOutcome.Succeeded, null, null, $"agent {agentId}");

    // Abort shortly after the current invocation returns, giving its result time to reach the Agent.
    private void ScheduleAbort()
    {
        HubCallerContext context = Context;
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
            context.Abort();
        });
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent {AgentId} connected to the control plane.")]
    private partial void LogConnected(AgentId agentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent {AgentId} disconnected from the control plane.")]
    private partial void LogDisconnected(AgentId agentId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Agent {AgentId} rejected at negotiation: {Reason}")]
    private partial void LogProtocolRejected(AgentId agentId, string reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Agent {AgentId} reported a state snapshot with {ServerCount} server(s).")]
    private partial void LogSnapshot(AgentId agentId, int serverCount);
}
