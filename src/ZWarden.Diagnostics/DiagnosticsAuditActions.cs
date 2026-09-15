namespace ZWarden.Diagnostics;

/// <summary>
/// The stable, machine-readable audit action names for the diagnostics engine (F29; F6, ADR 0019). A diagnostics
/// run is read-only, but running one is an authorized administrative action worth an audit trail: who ran a sweep,
/// when, and its headline verdict (a non-secret summary — never the untrusted per-check detail).
/// </summary>
public static class DiagnosticsAuditActions
{
    /// <summary>An operator ran a tenant-wide diagnostics sweep (authorized by <c>Diagnostics.View</c>).</summary>
    public const string Run = "Diagnostics.Run";

    /// <summary>An operator generated a sanitized support package (authorized by <c>Diagnostics.Export</c>; F30).
    /// A <c>Failed</c> outcome records a fail-closed abort — the secret scanner blocked generation (PRD 51). The
    /// detail carries the <c>DiagnosticId</c> and, on a block, the detector that fired — never the secret.</summary>
    public const string Export = "Diagnostics.Export";
}
