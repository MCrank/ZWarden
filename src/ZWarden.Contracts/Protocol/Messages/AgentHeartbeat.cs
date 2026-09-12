namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// Periodic liveness from the Agent (PRD 40). The envelope carries the protocol version and
/// timestamp; the body carries the Agent's coarse self-health. On heartbeat loss, ZWarden.Web keeps
/// the last observed state and marks it stale — it never promotes stale state back to current
/// (trust-boundaries.md §3).
/// </summary>
/// <param name="Health">The Agent's coarse self-reported health.</param>
[ProtocolMessage("agent.heartbeat")]
public sealed record AgentHeartbeat(AgentHealthStatus Health) : AgentEvent;
