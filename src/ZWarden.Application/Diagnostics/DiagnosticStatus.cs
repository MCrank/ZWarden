namespace ZWarden.Application.Diagnostics;

/// <summary>
/// The verdict of a single <see cref="DiagnosticCheck"/> (F29). A domain-level twin of the wire
/// <c>ProbeStatus</c> (ADR 0023) — the Application layer depends on no wire types, so PR-B maps the Agent
/// bundle's wire status onto this at the Web boundary. Ordered by severity so a report can roll several checks
/// up to their worst verdict.
/// </summary>
public enum DiagnosticStatus
{
    /// <summary>The check passed.</summary>
    Pass = 0,

    /// <summary>The check is non-fatally impaired — worth an operator's attention, but not a failure.</summary>
    Warn = 1,

    /// <summary>The check failed.</summary>
    Fail = 2,

    /// <summary>The check did not apply in this state (e.g. the TLS check on an HTTP-only deployment, or an
    /// Agent-side domain not yet gathered).</summary>
    Skipped = 3,
}
