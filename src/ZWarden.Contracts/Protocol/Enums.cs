namespace ZWarden.Contracts.Protocol;

/// <summary>
/// The Agent's coarse self-reported health, carried on every heartbeat (PRD 40). This is the
/// Agent process's own liveness, distinct from a Server's health (the richer per-Server health
/// model is F16's). Serialized by name, so a later addition is additive, never a silent renumber.
/// </summary>
public enum AgentHealthStatus
{
    /// <summary>Operating normally.</summary>
    Healthy = 0,

    /// <summary>Functional but impaired — some capability is degraded.</summary>
    Degraded = 1,

    /// <summary>Not able to carry out its duties.</summary>
    Unhealthy = 2,
}

/// <summary>
/// The coarse <i>observed</i> run-state of a Server as the Agent reports it in a state snapshot
/// (trust-boundaries.md §3 — observed, never inferred by Web). Deliberately minimal; F16 layers the
/// hierarchical health model on top. Serialized by name.
/// </summary>
public enum ServerRunState
{
    /// <summary>The Agent has no confirmed state for this Server.</summary>
    Unknown = 0,

    /// <summary>Not running.</summary>
    Stopped = 1,

    /// <summary>Starting up; not yet accepting players.</summary>
    Starting = 2,

    /// <summary>Running.</summary>
    Running = 3,

    /// <summary>Shutting down.</summary>
    Stopping = 4,

    /// <summary>Exited abnormally or failed to start.</summary>
    Failed = 5,
}

/// <summary>
/// A Server's hierarchical <b>health</b> as the Agent rolls it up from its container / process / startup /
/// network probes (F16) — the operator-facing taxonomy the issue names, and only these five. It is distinct
/// from <see cref="ServerRunState"/> (the coarse lifecycle position): a <see cref="ServerRunState.Running"/>
/// Server is <see cref="Healthy"/>, <see cref="Degraded"/> or <see cref="Failed"/> depending on the probes.
/// "Not heard from" is deliberately <b>not</b> a member — it is the staleness of the last report
/// (<c>Server.LastHealthReportedAt</c>), rendered by the UI. Serialized by name.
/// </summary>
public enum ServerHealth
{
    /// <summary>Not running — intentionally off. The healthy resting state of a stopped Server.</summary>
    Stopped = 0,

    /// <summary>Coming up: the process is alive but still inside its startup window, not yet accepting players.</summary>
    Starting = 1,

    /// <summary>Running and all probes pass.</summary>
    Healthy = 2,

    /// <summary>Running but impaired — a probe warns or a non-fatal check fails (e.g. a port is unreachable).</summary>
    Degraded = 3,

    /// <summary>Running abnormally or exited unexpectedly — a fatal probe failure (crash, OOM, unhealthy process).</summary>
    Failed = 4,
}

/// <summary>
/// One probe's verdict within a <see cref="Messages.HealthBreakdown"/> (F16). Serialized by name, so a later
/// addition is additive.
/// </summary>
public enum ProbeStatus
{
    /// <summary>The probe passed.</summary>
    Pass = 0,

    /// <summary>The probe is non-fatally impaired — degrades health but does not fail it.</summary>
    Warn = 1,

    /// <summary>The probe failed fatally.</summary>
    Fail = 2,

    /// <summary>The probe did not apply in this state (e.g. network probing a stopped container).</summary>
    Skipped = 3,
}

/// <summary>The terminal outcome of an Operation, reported by the Agent (PRD 18). Serialized by name.</summary>
public enum OperationOutcome
{
    /// <summary>The Operation completed successfully.</summary>
    Succeeded = 0,

    /// <summary>The Operation failed; see the accompanying reason.</summary>
    Failed = 1,
}
