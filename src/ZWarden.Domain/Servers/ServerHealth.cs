namespace ZWarden.Domain.Servers;

/// <summary>
/// The hierarchical <b>health</b> of a <see cref="Server"/> as ZWarden.Web last recorded it from an Agent
/// report — observed, never inferred (trust-boundaries.md §3). This is the Domain's own vocabulary: the wire
/// <c>ZWarden.Contracts.Protocol.ServerHealth</c> cannot be referenced here (the Domain references no
/// Contracts), so the reconciler in ZWarden.Web maps wire → this (F16). It is distinct from
/// <see cref="ServerRunState"/>: a <see cref="ServerRunState.Running"/> Server is <see cref="Healthy"/>,
/// <see cref="Degraded"/> or <see cref="Failed"/> depending on the probes. "Not heard from" is not a member —
/// it is the age of <see cref="Server.LastHealthReportedAt"/>. Stored by name, so a later addition is additive.
/// </summary>
public enum ServerHealth
{
    /// <summary>Not running — intentionally off.</summary>
    Stopped = 0,

    /// <summary>Coming up: alive but inside its startup window, not yet accepting players.</summary>
    Starting = 1,

    /// <summary>Running and all probes pass.</summary>
    Healthy = 2,

    /// <summary>Running but impaired — a non-fatal probe warns or fails.</summary>
    Degraded = 3,

    /// <summary>Running abnormally or exited unexpectedly — a fatal probe failure.</summary>
    Failed = 4,
}
