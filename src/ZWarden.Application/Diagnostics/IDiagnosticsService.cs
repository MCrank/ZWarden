using ZWarden.Domain.Ids;

namespace ZWarden.Application.Diagnostics;

/// <summary>Why a diagnostics run was refused (F29). Fail-closed (ADR 0018): the tenant-wide
/// <c>Diagnostics.View</c> permission is checked before any probe runs.</summary>
public enum DiagnosticsRunFailure
{
    /// <summary>The caller lacks the tenant-wide <c>Diagnostics.View</c> permission.</summary>
    NotAuthorized,
}

/// <summary>The outcome of a diagnostics run (F29): on success the transient <see cref="DiagnosticReport"/>;
/// otherwise a typed failure. The report is not persisted (F29 D-2).</summary>
public sealed record DiagnosticsRunResult(bool Succeeded, DiagnosticReport? Report, DiagnosticsRunFailure? Failure)
{
    public static DiagnosticsRunResult Success(DiagnosticReport report) => new(true, report, null);

    public static DiagnosticsRunResult Denied(DiagnosticsRunFailure failure) => new(false, null, failure);
}

/// <summary>
/// The tenant-wide diagnostics engine (F29): run a read-only sweep across the diagnostic domains and return one
/// <see cref="DiagnosticReport"/>. Fail-closed (ADR 0018): it authorizes the tenant-wide <c>Diagnostics.View</c>
/// permission before probing anything, audits the run, and never mutates a Server or its world. In PR-A only the
/// in-process domains (Web, Database, TLS, Agent connectivity) produce live verdicts; the Agent-gathered domains
/// appear as <see cref="DiagnosticStatus.Skipped"/> until F29 PR-B/PR-C wire the Agent gather. The report is
/// transient — F30 owns the durable, redacted, packaged form.
/// </summary>
public interface IDiagnosticsService
{
    /// <summary>Runs a tenant-wide diagnostics sweep for <paramref name="user"/>.</summary>
    Task<DiagnosticsRunResult> RunAsync(UserId user, CancellationToken cancellationToken = default);
}
