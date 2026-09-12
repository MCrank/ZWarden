namespace ZWarden.Contracts.Protocol;

/// <summary>
/// The marker every protocol payload carries. A payload is always an <see cref="AgentCommand"/>
/// (Web → Agent) or an <see cref="AgentEvent"/> (Agent → Web); this interface exists so an
/// <see cref="Envelope{TPayload}"/> can be spoken about, and dispatched, without naming a concrete
/// message type. It has no members: a protocol message is data, never behaviour.
/// </summary>
public interface IProtocolMessage;
