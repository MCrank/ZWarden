using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using ZWarden.Application.Agents;
using ZWarden.Application.Audit;
using ZWarden.Application.Backups;
using ZWarden.Application.Configuration;
using ZWarden.Application.Console;
using ZWarden.Application.Diagnostics;
using ZWarden.Application.Mods;
using ZWarden.Application.Operations;
using ZWarden.Application.Players;
using ZWarden.Application.Servers;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Agents;
using ZWarden.Infrastructure.Configuration;
using ZWarden.Web.Diagnostics;
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
    private readonly IHostCapacityCache _capacity;
    private readonly IServerHealthCache _healthCache;
    private readonly IServerLogBuffer _logBuffer;
    private readonly ServerConfigReadCoordinator _configReads;
    private readonly IPlayerRosterCache _rosters;
    private readonly IConsoleOutputCache _consoleOutput;
    private readonly IDiagnosticsResultCache _diagnostics;
    private readonly IConfigurationRevisionRecorder _configRevisions;
    private readonly IModInventoryCache _mods;
    private readonly IBackupRecorder _backups;
    private readonly ControlPlaneMetrics _telemetry;
    private readonly IAuditWriter _audit;
    private readonly ILogger<AgentHub> _logger;

    public AgentHub(
        IAgentConnectionRegistry registry,
        IAgentConnectionStateWriter state,
        IOperationStore operations,
        IServerStateReconciler servers,
        IServerMetricsCache metrics,
        IHostCapacityCache capacity,
        IServerHealthCache healthCache,
        IServerLogBuffer logBuffer,
        ServerConfigReadCoordinator configReads,
        IPlayerRosterCache rosters,
        IConsoleOutputCache consoleOutput,
        IDiagnosticsResultCache diagnostics,
        IConfigurationRevisionRecorder configRevisions,
        IModInventoryCache mods,
        IBackupRecorder backups,
        ControlPlaneMetrics telemetry,
        IAuditWriter audit,
        ILogger<AgentHub> logger)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(capacity);
        ArgumentNullException.ThrowIfNull(healthCache);
        ArgumentNullException.ThrowIfNull(logBuffer);
        ArgumentNullException.ThrowIfNull(configReads);
        ArgumentNullException.ThrowIfNull(rosters);
        ArgumentNullException.ThrowIfNull(consoleOutput);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(configRevisions);
        ArgumentNullException.ThrowIfNull(mods);
        ArgumentNullException.ThrowIfNull(backups);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(logger);
        _registry = registry;
        _state = state;
        _operations = operations;
        _servers = servers;
        _metrics = metrics;
        _capacity = capacity;
        _healthCache = healthCache;
        _logBuffer = logBuffer;
        _configReads = configReads;
        _rosters = rosters;
        _consoleOutput = consoleOutput;
        _diagnostics = diagnostics;
        _configRevisions = configRevisions;
        _mods = mods;
        _backups = backups;
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
            if (exception is not null)
            {
                // #232: an abnormal close (e.g. a message over the hub's receive limit) is never silent.
                LogDisconnectedWithError(agentId, exception);
            }
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

        HostDescriptor? host = hello.Payload.Host;
        HostFacts? facts = host is null ? null : new HostFacts(host.Hostname, host.AgentVersion, host.OsPlatform);
        await _state.MarkConnectedAsync(agentId, hello.ProtocolVersion, facts, Context.ConnectionAborted).ConfigureAwait(false);
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
            // Also cache the live rollup + reason + probe breakdown so the interactive detail panel can show it
            // without a tenant-scoped read. The reason and every probe detail are untrusted (trust-boundaries.md
            // §8), stored as data; the breakdown is transient like the rest of the cache (never persisted).
            _healthCache.Record(new ServerLiveHealth(
                agentId,
                change.Payload.ServerId,
                health,
                change.Payload.Reason,
                change.Timestamp,
                WireHealthBreakdown.ToLive(change.Payload.Breakdown)));
            _telemetry.RecordHealthTransition(change.Payload.Health);
        }
    }

    /// <summary>
    /// The Agent's periodic runtime-metrics report (F16): the latest CPU/memory/disk sample per Server. Metrics
    /// are transient — recorded in the in-memory <see cref="IServerMetricsCache"/> (latest-sample-only) and pushed
    /// to the live UI, never persisted or audited. Each sample is stamped with the reporting Agent so the cache
    /// can refuse a sample forged for a Server this Agent does not own (trust-boundaries.md §8). Two facts are
    /// persisted: the manifest build id (#257) and the game version from the boot log (#262). When either differs
    /// from this Agent's previous sample (or there is none, e.g. after a Web restart) it is recorded on the owned
    /// Server, so the DB is touched only on a change.
    /// </summary>
    public Task HostCapacity(Envelope<HostCapacityReport> report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (!AgentClaims.TryGetAgentId(Context.User, out AgentId agentId))
        {
            return Task.CompletedTask;
        }

        HostCapacityReport r = report.Payload;
        // Untrusted: a nonsensical (negative) figure is dropped rather than shown as guidance.
        if (r.TotalMemoryBytes < 0 || r.CommittedMemoryBytes < 0 || r.MemoryOverheadBytes < 0
            || r.DefaultHeapSizeBytes < 0 || r.ReserveMemoryBytes < 0)
        {
            return Task.CompletedTask;
        }

        _capacity.Record(new Application.Servers.HostCapacity(
            agentId, r.TotalMemoryBytes, r.CommittedMemoryBytes, r.MemoryOverheadBytes, r.DefaultHeapSizeBytes,
            r.ReserveMemoryBytes, report.Timestamp));
        return Task.CompletedTask;
    }

    public async Task MetricsReport(Envelope<ServerMetricsReport> report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (!AgentClaims.TryGetAgentId(Context.User, out AgentId agentId))
        {
            return;
        }

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
                s.SampledAt,
                s.PlayerCountSampledAt,
                s.StartedAt,
                s.InstalledBuildId,
                s.GameVersion))
            .ToList();

        List<(ServerId ServerId, string BuildId)> changedBuilds = mapped
            .Where(m => m.InstalledBuildId is not null
                && _metrics.GetLatest(m.ServerId, agentId)?.InstalledBuildId != m.InstalledBuildId)
            .Select(m => (m.ServerId, m.InstalledBuildId!))
            .ToList();
        List<(ServerId ServerId, string GameVersion)> changedVersions = mapped
            .Where(m => m.GameVersion is not null
                && _metrics.GetLatest(m.ServerId, agentId)?.GameVersion != m.GameVersion)
            .Select(m => (m.ServerId, m.GameVersion!))
            .ToList();

        _metrics.Record(mapped);

        foreach ((ServerId serverId, string buildId) in changedBuilds)
        {
            await _servers.RecordReportedBuildAsync(agentId, serverId, buildId, Context.ConnectionAborted)
                .ConfigureAwait(false);
        }

        foreach ((ServerId serverId, string gameVersion) in changedVersions)
        {
            await _servers.RecordReportedGameVersionAsync(agentId, serverId, gameVersion, Context.ConnectionAborted)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The Agent's live log batch while an operator is watching (F27): sanitized stdout/stderr lines. Logs are
    /// transient — appended to the in-memory <see cref="IServerLogBuffer"/> (a bounded per-Server tail) and polled
    /// by the live panel, never persisted or audited. The batch is stamped with the reporting Agent so the buffer
    /// partitions by owner and a batch forged for a Server this Agent does not own cannot surface under it
    /// (trust-boundaries.md §8). Line text is untrusted; carried verbatim and rendered as data.
    /// </summary>
    public Task ServerLogBatch(Envelope<ServerLogBatch> batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (AgentClaims.TryGetAgentId(Context.User, out AgentId agentId))
        {
            List<ServerLogLineView> lines = batch.Payload.Lines
                .Select(l => new ServerLogLineView(
                    l.Sequence, l.Timestamp, l.Stream == LogStreamKind.Stderr, l.Text, l.Truncated))
                .ToList();
            _logBuffer.Append(agentId, batch.Payload.ServerId, lines, batch.Payload.Dropped);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// The Agent's reply chunk to a live configuration read (F20c, ADR 0041). Handed to the read coordinator to be
    /// reassembled against its pending request. The chunk is stamped with the reporting Agent (trusted off the
    /// connection principal) so the coordinator accepts it only for a read routed to that Agent — a reply forged for
    /// another Agent's read is dropped (trust-boundaries.md §8). Transient: nothing is persisted or audited.
    /// </summary>
    public Task ServerConfigContent(Envelope<ServerConfigContent> content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (AgentClaims.TryGetAgentId(Context.User, out AgentId agentId))
        {
            _configReads.AcceptChunk(agentId, content);
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

        // A provisioning or recreate Operation carries the container facts the Agent observed; record the Server's
        // container linkage before marking the operation done (F14 PR-B). A rolled-back recreate (#229) fails yet still
        // reports the pair and the rollback container the Server ended on, so this is recorded whatever the outcome.
        // Observed, tenant-scoped.
        if (completed.Payload.Provision is { } provision && completed.ServerId is { } serverId)
        {
            await _servers.RecordProvisionedAsync(
                serverId, provision.GamePort, provision.QueryPort, provision.ContainerId, provision.HeapSizeBytes,
                Context.ConnectionAborted)
                .ConfigureAwait(false);
        }

        if (completed.Payload.Outcome == OperationOutcome.Succeeded)
        {
            // A successful update Operation carries the build id the Agent read from the manifest (F17); record
            // it against the Server before the operation is marked done. Observed, tenant-scoped.
            if (completed.Payload.Update is { } update && completed.ServerId is { } updatedServerId)
            {
                await _servers.RecordInstalledBuildAsync(updatedServerId, update.InstalledBuildId, Context.ConnectionAborted)
                    .ConfigureAwait(false);
            }

            // A successful enumeration carries the roster the Agent observed over RCON (F19); cache the newest
            // per Server for the live UI island, keyed by the reporting Agent (the ownership guard, §8). Transient
            // display data — never persisted. The usernames are untrusted and carried verbatim (escaped at render).
            if (completed.Payload.Roster is { } roster && completed.ServerId is { } rosterServerId
                && AgentClaims.TryGetAgentId(Context.User, out AgentId reportingAgent))
            {
                _rosters.Record(new PlayerRoster(
                    rosterServerId, reportingAgent, roster.Count, roster.Players, completed.Timestamp));
            }

            // A successful console command carries the reply the Agent observed over RCON (F28); cache the newest
            // outputs per Server for the live console pane, keyed by the reporting Agent (the ownership guard, §8).
            // Transient display data — never persisted (the audit trail is the durable history). The output is
            // untrusted and carried verbatim (escaped at render).
            if (completed.Payload.ConsoleCommand is { } consoleResult && completed.ServerId is { } consoleServerId
                && AgentClaims.TryGetAgentId(Context.User, out AgentId consoleAgent))
            {
                _consoleOutput.Record(
                    consoleServerId, consoleAgent, operationId, consoleResult.Output, consoleResult.Truncated, completed.Timestamp);
            }

            // A successful host diagnostics gather carries the host-level checks the Agent observed (F29); cache the
            // newest bundle keyed by the reporting Agent (the ownership key). Transient — never persisted (a run is
            // transient, F29 D-2). Each check's detail is untrusted, carried verbatim (escaped at render).
            if (completed.Payload.HostDiagnostics is { } hostDiagnostics
                && AgentClaims.TryGetAgentId(Context.User, out AgentId hostAgent))
            {
                _diagnostics.RecordHost(
                    hostAgent, new DiagnosticBundle(WireDiagnostics.ToChecks(hostDiagnostics.Checks), completed.Timestamp));
            }

            // A successful per-server diagnostics gather carries the server-level checks (F29); cache the newest
            // bundle per Server, keyed by the reporting Agent (the ownership guard, §8). Transient — never persisted.
            if (completed.Payload.ServerDiagnostics is { } serverDiagnostics && completed.ServerId is { } diagServerId
                && AgentClaims.TryGetAgentId(Context.User, out AgentId diagAgent))
            {
                _diagnostics.RecordServer(
                    diagServerId, diagAgent, new DiagnosticBundle(WireDiagnostics.ToChecks(serverDiagnostics.Checks), completed.Timestamp));
            }

            // A successful configuration apply carries the revision the Agent recorded from the file after the
            // BOM-less atomic write (F20b, ADR 0011); persist it as the new drift baseline before marking the
            // operation done. Observed, tenant-scoped, unattributed (the audit trail carries who applied it).
            if (completed.Payload.Config is { } config && completed.ServerId is { } configServerId)
            {
                await _configRevisions.RecordAsync(
                    configServerId, config.File, config.CanonicalSnapshot, config.SnapshotHash, Context.ConnectionAborted)
                    .ConfigureAwait(false);

                // Whether the change is live (#225): the Operation's result line says so, and an attempted INI reload
                // is audited. The Agent's detail is untrusted display text (bounded by the status-line cap).
                if (ConfigReloadText.Describe(config) is { } summary)
                {
                    await _operations.ApplyProgressAsync(operationId, 100, summary, Context.ConnectionAborted)
                        .ConfigureAwait(false);
                }

                if (config.Reload is ConfigReloadOutcome.Reloaded or ConfigReloadOutcome.Failed or ConfigReloadOutcome.NotRunning)
                {
                    await _audit.WriteAsync(
                        new AuditEntry(
                            ConfigurationAuditActions.LiveReload,
                            config.Reload == ConfigReloadOutcome.Failed ? AuditOutcome.Failed : AuditOutcome.Succeeded,
                            ServerId: configServerId,
                            Detail: ConfigReloadText.Describe(config)),
                        Context.ConnectionAborted).ConfigureAwait(false);
                }
            }

            // A successful mod discovery carries the Workshop-and-mod inventory the Agent observed on disk (F21);
            // cache the newest per Server for the live UI, keyed by the reporting Agent (the ownership guard, §8).
            // Transient display data — never persisted. Ids/names are untrusted, carried verbatim (escaped at render).
            if (completed.Payload.Mods is { } mods && completed.ServerId is { } modsServerId
                && AgentClaims.TryGetAgentId(Context.User, out AgentId modsAgent))
            {
                _mods.Record(ToInventory(modsServerId, modsAgent, mods, completed.Timestamp));
            }

            // A successful backup carries the archive facts the Agent wrote host-side (F24); persist a tenant-owned
            // Backup, scoped to the reporting Agent's own Server (the ownership guard, §3), before marking the
            // operation done. The retention reason is read from the Operation's command payload.
            if (completed.Payload.Backup is { } backupResult && completed.ServerId is { } backupServerId
                && AgentClaims.TryGetAgentId(Context.User, out AgentId backupAgent))
            {
                await _backups.RecordCreatedAsync(
                    backupServerId, backupAgent, operationId,
                    backupResult.ArchiveName, backupResult.SizeBytes, backupResult.Sha256, backupResult.CreatedAt,
                    Context.ConnectionAborted).ConfigureAwait(false);
            }

            // A successful backup deletion signals the Agent removed the archive (F24); remove the backup record,
            // resolving its id from the Operation's command payload.
            if (completed.Payload.BackupDeletion is not null)
            {
                await _backups.RecordDeletedAsync(operationId, Context.ConnectionAborted).ConfigureAwait(false);
            }

            // A successful restore carries the protective backup the Agent took of the pre-restore world (F25); persist
            // it as a tenant-owned PreOperation Backup, scoped to the reporting Agent's own Server (ownership guard, §3),
            // so a mistaken restore can itself be rolled back. The world swap itself happened inside the Operation.
            if (completed.Payload.Restore is { } restoreResult && completed.ServerId is { } restoreServerId
                && AgentClaims.TryGetAgentId(Context.User, out AgentId restoreAgent))
            {
                await _backups.RecordRestoreProtectiveBackupAsync(
                    restoreServerId, restoreAgent,
                    restoreResult.ProtectiveBackup.ArchiveName, restoreResult.ProtectiveBackup.SizeBytes,
                    restoreResult.ProtectiveBackup.Sha256, restoreResult.ProtectiveBackup.CreatedAt,
                    Context.ConnectionAborted).ConfigureAwait(false);
            }

            await _operations.CompleteSucceededAsync(operationId, Context.ConnectionAborted).ConfigureAwait(false);
        }
        else
        {
            await _operations.CompleteFailedAsync(operationId, completed.Payload.FailureReason, Context.ConnectionAborted)
                .ConfigureAwait(false);
        }
    }

    // Maps the wire discovery result onto the Application-side observed inventory (F21), so the cache and the UI
    // never touch the protocol contracts. Ids and names cross verbatim (untrusted, escaped at render).
    private static ModInventory ToInventory(ServerId server, AgentId agent, ModDiscoveryResult result, DateTimeOffset at) =>
        new(
            server,
            agent,
            [.. result.InstalledItems.Select(i =>
                new InstalledWorkshopItem(i.WorkshopId, [.. i.Mods.Select(m => new InstalledMod(
                    m.ModId, m.Name, m.Version, m.PzVersion, m.VersionMin, m.Requires, m.Incompatible, m.Tags))]))],
            result.ConfiguredWorkshopIds,
            result.EnabledModIds,
            [.. result.Findings.Select(f => new ModCompatIssue(ToIssueKind(f.Kind), f.Subject, f.Detail))],
            at);

    private static ModCompatIssueKind ToIssueKind(ModCompatKind kind) => kind switch
    {
        ModCompatKind.ReferencedNotInstalled => ModCompatIssueKind.ReferencedNotInstalled,
        ModCompatKind.EnabledButMissing => ModCompatIssueKind.EnabledButMissing,
        ModCompatKind.InstalledButInactive => ModCompatIssueKind.InstalledButInactive,
        ModCompatKind.DuplicateModId => ModCompatIssueKind.DuplicateModId,
        ModCompatKind.RequiresMissing => ModCompatIssueKind.RequiresMissing,
        ModCompatKind.IncompatiblePresent => ModCompatIssueKind.IncompatiblePresent,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown mod compatibility kind."),
    };

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Agent {AgentId} disconnected abnormally.")]
    private partial void LogDisconnectedWithError(AgentId agentId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Agent {AgentId} rejected at negotiation: {Reason}")]
    private partial void LogProtocolRejected(AgentId agentId, string reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Agent {AgentId} reported a state snapshot with {ServerCount} server(s).")]
    private partial void LogSnapshot(AgentId agentId, int serverCount);
}
