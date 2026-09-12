namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// The terminal report for an Operation (PRD 18). The Operation it concerns is the envelope's
/// <see cref="Envelope{TPayload}.OperationId"/>; F11 turns this into the operation's final state.
/// This reports what the Agent <i>observed</i> — a success here means the command was carried out,
/// not that any higher-level state is now true (trust-boundaries.md §3).
/// </summary>
/// <param name="Outcome">Whether the Operation succeeded or failed.</param>
/// <param name="FailureReason">
/// On failure, an optional <b>untrusted</b> reason (trust-boundaries.md §8); <c>null</c> on success.
/// </param>
[ProtocolMessage("operation.completed")]
public sealed record OperationCompleted(OperationOutcome Outcome, string? FailureReason = null) : AgentEvent;
