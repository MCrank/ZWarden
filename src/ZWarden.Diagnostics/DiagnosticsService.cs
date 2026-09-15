using ZWarden.Application.Audit;
using ZWarden.Application.Authorization;
using ZWarden.Application.Diagnostics;
using ZWarden.Diagnostics.Evaluators;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;

namespace ZWarden.Diagnostics;

/// <summary>
/// The tenant-wide diagnostics engine (F29 — see <see cref="IDiagnosticsService"/>). It authorizes the
/// tenant-wide <c>Diagnostics.View</c> permission fail-closed (ADR 0018), gathers the in-process domain facts
/// through the Application probe ports, runs the pure evaluators, assembles one transient
/// <see cref="DiagnosticReport"/>, and audits the run. Every check is read-only — nothing here mutates a Server,
/// its world, or its configuration. The Agent-gathered domains are reported <see cref="DiagnosticStatus.Skipped"/>
/// until F29 PR-B/PR-C wire the Agent gather.
/// </summary>
public sealed class DiagnosticsService : IDiagnosticsService
{
    // The domains that a live Agent gathers (F29 PR-B/PR-C). In PR-A they are reported as Skipped so the report's
    // shape is complete and the UI can show every domain from the first slice.
    private static readonly DiagnosticDomain[] PendingAgentDomains =
    [
        DiagnosticDomain.Docker,
        DiagnosticDomain.Rcon,
        DiagnosticDomain.GamePort,
        DiagnosticDomain.Filesystem,
        DiagnosticDomain.SteamCmd,
        DiagnosticDomain.Mod,
        DiagnosticDomain.Config,
        DiagnosticDomain.Compatibility,
    ];

    private readonly IPermissionChecker _permissions;
    private readonly IDiagnosticsDbProbe _database;
    private readonly IDiagnosticsTlsProbe _tls;
    private readonly IDiagnosticsAgentPresenceProbe _agents;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _time;
    private readonly DiagnosticsOptions _options;

    public DiagnosticsService(
        IPermissionChecker permissions,
        IDiagnosticsDbProbe database,
        IDiagnosticsTlsProbe tls,
        IDiagnosticsAgentPresenceProbe agents,
        IAuditWriter audit,
        TimeProvider time,
        DiagnosticsOptions options)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(tls);
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(options);
        _permissions = permissions;
        _database = database;
        _tls = tls;
        _agents = agents;
        _audit = audit;
        _time = time;
        _options = options;
    }

    /// <inheritdoc />
    public async Task<DiagnosticsRunResult> RunAsync(UserId user, CancellationToken cancellationToken = default)
    {
        // Fail-closed: authorize the tenant-wide Diagnostics.View before any probe runs (ADR 0018).
        AuthorizationDecision decision = await _permissions
            .EvaluateAsync(user, Permissions.DiagnosticsView, server: null, cancellationToken).ConfigureAwait(false);
        if (!decision.IsAllowed)
        {
            return DiagnosticsRunResult.Denied(DiagnosticsRunFailure.NotAuthorized);
        }

        DateTimeOffset now = _time.GetUtcNow();

        DatabaseProbeFacts dbFacts = await _database.ProbeAsync(cancellationToken).ConfigureAwait(false);
        TlsProbeFacts tlsFacts = await _tls.ProbeAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<AgentPresenceFacts> agentFacts = await _agents.ProbeAsync(cancellationToken).ConfigureAwait(false);

        List<DiagnosticCheck> checks =
        [
            WebSelfDiagnostic.Evaluate(_options.Version),
            DatabaseDiagnostic.Evaluate(dbFacts),
            TlsDiagnostic.Evaluate(tlsFacts, now, _options.TlsWarnWindow),
            AgentConnectivityDiagnostic.Evaluate(agentFacts, now, _options.AgentStaleWindow),
        ];

        foreach (DiagnosticDomain domain in PendingAgentDomains)
        {
            checks.Add(DiagnosticCheck.Create(
                domain,
                DiagnosticStatus.Skipped,
                "Not gathered in this run.",
                "Agent-side diagnostics arrive in F29 PR-B/PR-C."));
        }

        DiagnosticReport report = new(checks, now);

        await _audit.WriteAsync(
            new AuditEntry(DiagnosticsAuditActions.Run, AuditOutcome.Succeeded, user, ServerId: null, $"worst={report.Worst()}; {checks.Count} checks"),
            cancellationToken).ConfigureAwait(false);

        return DiagnosticsRunResult.Success(report);
    }
}
