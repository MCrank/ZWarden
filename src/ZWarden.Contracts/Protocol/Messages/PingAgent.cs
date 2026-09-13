namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// A non-mutating round-trip probe of a Server's Agent (F11): the Agent replies with
/// <see cref="OperationProgress"/> (optionally) and then <see cref="OperationCompleted"/> on the same
/// <see cref="Envelope{TPayload}.OperationId"/>. It is the first tool for "is this Agent actually
/// round-tripping commands right now?" and the vehicle that exercises the whole operations engine
/// end-to-end before the real mutating commands (<c>RestartServer</c> → F15, …) exist. It carries no
/// payload — the operation it belongs to is the envelope's <c>OperationId</c> — and, being non-mutating,
/// never claims the per-server lock. This is the first concrete leaf of the closed
/// <see cref="AgentCommand"/> vocabulary (trust-boundaries.md §9 rule 3); it carries no free-form command.
/// </summary>
[ProtocolMessage("diagnostics.ping")]
public sealed record PingAgent : AgentCommand;
