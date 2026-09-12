using ZWarden.Contracts.Protocol;

namespace ZWarden.Agent.Health;

/// <summary>
/// The Agent's in-process self-health (F8), modelled with F7's <see cref="AgentHealthStatus"/> — no
/// Agent-local duplicate of the enum. Components set it with a reason; diagnostics read it, and F10
/// later puts it on the heartbeat. This is the Agent process's own liveness, distinct from a
/// Server's health (F16).
/// </summary>
public interface IAgentHealthState
{
    /// <summary>The current self-reported health.</summary>
    AgentHealthStatus Current { get; }

    /// <summary>Why the Agent is in its current health state.</summary>
    string Reason { get; }

    /// <summary>Records a new health state and the reason for it, logging any transition.</summary>
    void Report(AgentHealthStatus status, string reason);
}
