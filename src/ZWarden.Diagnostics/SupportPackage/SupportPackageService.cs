using ZWarden.Application.Audit;
using ZWarden.Application.Authorization;
using ZWarden.Application.Diagnostics;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;

namespace ZWarden.Diagnostics.SupportPackage;

/// <summary>
/// The support-package orchestrator (F30 — see <see cref="ISupportPackageService"/>). It authorizes the tenant-wide
/// <c>Diagnostics.Export</c> permission fail-closed (ADR 0018), collects the transient F29 report through
/// <see cref="IDiagnosticsService"/> and the environment facts, runs the pure pipeline, and audits the outcome
/// (ADR 0019) — a success with the <c>DiagnosticId</c> and worst status, or a fail-closed abort naming the detector
/// that blocked it (never the secret). It writes nothing to disk; the ZIP and the download are the Web shell's job.
/// </summary>
public sealed class SupportPackageService : ISupportPackageService
{
    private readonly IPermissionChecker _permissions;
    private readonly IDiagnosticsService _diagnostics;
    private readonly ISupportPackageBuilder _builder;
    private readonly IEnvironmentFactsProvider _environment;
    private readonly IAuditWriter _audit;

    public SupportPackageService(
        IPermissionChecker permissions,
        IDiagnosticsService diagnostics,
        ISupportPackageBuilder builder,
        IEnvironmentFactsProvider environment,
        IAuditWriter audit)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(audit);
        _permissions = permissions;
        _diagnostics = diagnostics;
        _builder = builder;
        _environment = environment;
        _audit = audit;
    }

    /// <inheritdoc />
    public async Task<SupportPackageResult> CreateTenantPackageAsync(UserId user, CancellationToken cancellationToken = default)
    {
        if (!await IsAuthorizedAsync(user, cancellationToken).ConfigureAwait(false))
        {
            return SupportPackageResult.Denied(SupportPackageFailure.NotAuthorized);
        }

        DiagnosticsRunResult run = await _diagnostics.RunAsync(user, cancellationToken).ConfigureAwait(false);
        if (!run.Succeeded)
        {
            return SupportPackageResult.Denied(SupportPackageFailure.NotAuthorized);
        }

        return await BuildAndAuditAsync(user, run.Report!, "tenant", serverId: null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<SupportPackageResult> CreateServerPackageAsync(
        UserId user, ServerId serverId, AgentId owningAgentId, CancellationToken cancellationToken = default)
    {
        if (!await IsAuthorizedAsync(user, cancellationToken).ConfigureAwait(false))
        {
            return SupportPackageResult.Denied(SupportPackageFailure.NotAuthorized);
        }

        DiagnosticsRunResult run = await _diagnostics
            .RunForServerAsync(user, serverId, owningAgentId, cancellationToken).ConfigureAwait(false);
        if (!run.Succeeded)
        {
            return SupportPackageResult.Denied(SupportPackageFailure.NotAuthorized);
        }

        return await BuildAndAuditAsync(user, run.Report!, "server", serverId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> IsAuthorizedAsync(UserId user, CancellationToken cancellationToken)
    {
        // Fail-closed: authorize the tenant-wide Diagnostics.Export before any report is collected (ADR 0018).
        AuthorizationDecision decision = await _permissions
            .EvaluateAsync(user, Permissions.DiagnosticsExport, server: null, cancellationToken).ConfigureAwait(false);
        return decision.IsAllowed;
    }

    private async Task<SupportPackageResult> BuildAndAuditAsync(
        UserId user, DiagnosticReport report, string scope, ServerId? serverId, CancellationToken cancellationToken)
    {
        SupportPackageContent content = new(report, _environment.Capture(), scope, SupportPackageContent.NoTargets);
        SupportPackageResult result = _builder.Build(content);

        AuditEntry entry = result switch
        {
            { Succeeded: true, Package: { } package } => new AuditEntry(
                DiagnosticsAuditActions.Export, AuditOutcome.Succeeded, user, serverId,
                $"diagnosticId={package.Manifest.DiagnosticId}; scope={scope}; worst={package.Manifest.WorstStatus}; scan=clean"),

            // Fail-closed abort: name the detector that blocked it, never the secret (PRD 51, D-3).
            { Failure: SupportPackageFailure.SecretDetected, Detection: { } detection } => new AuditEntry(
                DiagnosticsAuditActions.Export, AuditOutcome.Failed, user, serverId,
                $"blocked: secret detected ({detection.DetectorName}); scope={scope}; nothing emitted"),

            _ => new AuditEntry(
                DiagnosticsAuditActions.Export, AuditOutcome.Failed, user, serverId,
                $"failed: {result.Failure}; scope={scope}"),
        };

        await _audit.WriteAsync(entry, cancellationToken).ConfigureAwait(false);
        return result;
    }
}
