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
/// <param name="Provision">
/// For a successful provisioning Operation (<see cref="CreateServer"/>), the container facts the Agent
/// observed — the allocated ports and the created container id — so ZWarden.Web can record the Server's
/// container linkage. <c>null</c> for every other Operation. Additive and optional (ADR 0020); observed
/// data, recorded only against the Server the envelope's <c>ServerId</c> names, in the current tenant.
/// </param>
[ProtocolMessage("operation.completed")]
public sealed record OperationCompleted(
    OperationOutcome Outcome,
    string? FailureReason = null,
    ProvisionResult? Provision = null) : AgentEvent;

/// <summary>The container facts a successful <see cref="CreateServer"/> Operation observed (F14 PR-B): the two
/// allocated host UDP ports and the created Docker container id. Non-secret; the Server it belongs to is the
/// completion envelope's <c>ServerId</c>.</summary>
/// <param name="GamePort">The allocated game UDP port.</param>
/// <param name="QueryPort">The allocated query/direct-connect UDP port.</param>
/// <param name="ContainerId">The created container's Docker id (observed; untrusted, stored length-bounded).</param>
public sealed record ProvisionResult(int GamePort, int QueryPort, string ContainerId);
