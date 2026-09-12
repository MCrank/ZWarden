namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// Progress for an in-flight Operation (PRD 18). The Operation it concerns is the envelope's
/// <see cref="Envelope{TPayload}.OperationId"/>; F11 turns a stream of these into operation
/// progress. <see cref="StatusLine"/> is <b>untrusted</b>, Agent/PZ-originated text
/// (trust-boundaries.md §8) — never markup, never an identifier; escape it at render.
/// </summary>
/// <param name="PercentComplete">
/// Progress from 0 to 100. Treated as advisory: a consumer clamps rather than trusting the range,
/// since it originates outside the control plane.
/// </param>
/// <param name="StatusLine">An optional, untrusted human-readable status note.</param>
[ProtocolMessage("operation.progress")]
public sealed record OperationProgress(int PercentComplete, string? StatusLine = null) : AgentEvent;
