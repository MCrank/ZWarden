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
    private readonly IDiagnosticsResultCache _cache;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _time;
    private readonly DiagnosticsOptions _options;

    public DiagnosticsService(
        IPermissionChecker permissions,
        IDiagnosticsDbProbe database,
        IDiagnosticsTlsProbe tls,
        IDiagnosticsAgentPresenceProbe agents,
        IDiagnosticsResultCache cache,
        IAuditWriter audit,
        TimeProvider time,
        DiagnosticsOptions options)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(tls);
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(options);
        _permissions = permissions;
        _database = database;
        _tls = tls;
        _agents = agents;
        _cache = cache;
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

        // Merge the latest host-level gather each connected Agent reported (F29 PR-B). The bundles are cached from
        // GatherHostDiagnostics completions; a domain with no cached bundle yet falls back to a Skipped placeholder,
        // so the report always covers every domain. Per-server domains (RCON, game port, SteamCMD, and the PR-C
        // content domains) are surfaced by the per-server run.
        HashSet<DiagnosticDomain> covered = [];
        foreach (AgentPresenceFacts agent in agentFacts)
        {
            if (!agent.IsConnected || _cache.GetHost(agent.AgentId) is not { } bundle)
            {
                continue;
            }

            checks.AddRange(bundle.Checks);
            foreach (DiagnosticCheck check in bundle.Checks)
            {
                covered.Add(check.Domain);
            }
        }

        foreach (DiagnosticDomain domain in PendingAgentDomains)
        {
            if (!covered.Contains(domain))
            {
                checks.Add(DiagnosticCheck.Create(
                    domain,
                    DiagnosticStatus.Skipped,
                    "Not gathered in this run.",
                    "Trigger a diagnostics gather to fill this domain (F29)."));
            }
        }

        DiagnosticReport report = new(checks, now);

        await _audit.WriteAsync(
            new AuditEntry(DiagnosticsAuditActions.Run, AuditOutcome.Succeeded, user, ServerId: null, $"worst={report.Worst()}; {checks.Count} checks"),
            cancellationToken).ConfigureAwait(false);

        return DiagnosticsRunResult.Success(report);
    }

    // The domains a per-server report covers: the Server's own gather plus its owning Agent's host domains.
    private static readonly DiagnosticDomain[] ServerReportDomains =
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

    /// <inheritdoc />
    public async Task<DiagnosticsRunResult> RunForServerAsync(
        UserId user, ServerId serverId, AgentId owningAgentId, CancellationToken cancellationToken = default)
    {
        AuthorizationDecision decision = await _permissions
            .EvaluateAsync(user, Permissions.DiagnosticsView, server: null, cancellationToken).ConfigureAwait(false);
        if (!decision.IsAllowed)
        {
            return DiagnosticsRunResult.Denied(DiagnosticsRunFailure.NotAuthorized);
        }

        DateTimeOffset now = _time.GetUtcNow();
        List<DiagnosticCheck> checks = [];
        HashSet<DiagnosticDomain> covered = [];

        // The Server's own gather (RCON, game port, filesystem, SteamCMD, mods, config, compatibility).
        if (_cache.GetServer(serverId, owningAgentId) is { } serverBundle)
        {
            Merge(checks, covered, serverBundle);
        }

        // The owning Agent's host gather (Docker, host filesystem) — the host the Server runs on.
        if (_cache.GetHost(owningAgentId) is { } hostBundle)
        {
            Merge(checks, covered, hostBundle);
        }

        foreach (DiagnosticDomain domain in ServerReportDomains)
        {
            if (!covered.Contains(domain))
            {
                checks.Add(DiagnosticCheck.Create(
                    domain, DiagnosticStatus.Skipped, "Not gathered yet.", "Trigger a diagnostics gather to fill this domain (F29)."));
            }
        }

        return DiagnosticsRunResult.Success(new DiagnosticReport(checks, now));
    }

    private static void Merge(List<DiagnosticCheck> checks, HashSet<DiagnosticDomain> covered, DiagnosticBundle bundle)
    {
        checks.AddRange(bundle.Checks);
        foreach (DiagnosticCheck check in bundle.Checks)
        {
            covered.Add(check.Domain);
        }
    }
}
