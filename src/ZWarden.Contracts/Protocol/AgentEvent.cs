namespace ZWarden.Contracts.Protocol;

/// <summary>
/// The closed root of the <b>Agent → Web</b> report vocabulary (PRD 18, trust-boundaries.md §3).
/// Events carry what the Agent has <i>observed</i> — liveness, state snapshots, operation progress
/// and completion — upward. ZWarden.Web is authoritative for <i>desired</i> state; an event is the
/// only way <i>observed</i> state enters the control plane, and Web must never infer a transition
/// from a command's mere success (trust-boundaries.md §3).
/// </summary>
/// <remarks>
/// F7's five lifecycle events derive from this root. Later features add their own reports
/// (<c>ServerStateChanged</c>/<c>HealthChanged</c> → F16, <c>LogEntry</c> → F27, player events →
/// F19), each a <c>sealed record</c> with a <see cref="ProtocolMessageAttribute"/>.
/// </remarks>
public abstract record AgentEvent : IProtocolMessage;
