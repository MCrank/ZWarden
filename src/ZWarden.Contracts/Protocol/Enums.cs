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

/// <summary>The terminal outcome of an Operation, reported by the Agent (PRD 18). Serialized by name.</summary>
public enum OperationOutcome
{
    /// <summary>The Operation completed successfully.</summary>
    Succeeded = 0,

    /// <summary>The Operation failed; see the accompanying reason.</summary>
    Failed = 1,
}
