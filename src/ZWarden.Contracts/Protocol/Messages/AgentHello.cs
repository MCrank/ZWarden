using ZWarden.Domain.Ids;

namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// The first message an Agent sends on connect (PRD 18). It names the Agent; the protocol version
/// used for negotiation is the envelope's <see cref="Envelope{TPayload}.ProtocolVersion"/>, which
/// F10 checks with <see cref="ProtocolCompatibility"/> before accepting the connection. F7 does not
/// authenticate the Agent — enrollment and credentials are F9's.
/// </summary>
/// <param name="AgentId">The Agent identifying itself.</param>
[ProtocolMessage("agent.hello")]
public sealed record AgentHello(AgentId AgentId) : AgentEvent;
