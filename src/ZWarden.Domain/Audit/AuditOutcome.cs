namespace ZWarden.Domain.Audit;

/// <summary>
/// The outcome of an audited occurrence (ADR 0019). A small, closed, stable set — unlike the open
/// <c>Action</c> string — so the viewer and future exporters can filter on it without source archaeology.
/// </summary>
public enum AuditOutcome
{
    /// <summary>The occurrence completed successfully (e.g. a sign-in, a role created).</summary>
    Succeeded,

    /// <summary>The occurrence was attempted and failed (e.g. a wrong-password sign-in, a lockout).</summary>
    Failed,

    /// <summary>The occurrence was refused by an authorization decision (fail-closed deny).</summary>
    Denied,
}
