namespace ZWarden.Contracts.Protocol;

/// <summary>
/// The closed root of the <b>Web → Agent</b> command vocabulary (PRD 19, trust-boundaries.md §3).
/// A command is a request to the Agent to do something to a Server, always structured and always
/// from a known, finite set — <b>never a free-form command, script or shell string</b>
/// (trust-boundaries.md §9 rule 3; the <c>ClosedCommandVocabularyTests</c> fail the build if one
/// is introduced). <c>ExecuteShellCommand(string)</c> cannot exist here under any name.
/// </summary>
/// <remarks>
/// F7 defines the root and the guarantee; concrete commands land with the feature that owns them
/// (<c>RestartServer</c> → F15, <c>KickPlayer</c> → F19, …), each a <c>sealed record</c> deriving
/// from this type and carrying a <see cref="ProtocolMessageAttribute"/>. Every command carries an
/// <c>OperationId</c> in its envelope so the Agent can enforce idempotency (PRD 20).
/// </remarks>
public abstract record AgentCommand : IProtocolMessage;
