using ZWarden.Domain.Ids;

namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// The Agent reports that a Server's hierarchical <b>health</b> rollup changed (F16; reserved on
/// <see cref="AgentEvent"/>). It carries the new <see cref="ServerHealth"/>, a short untrusted reason, and the
/// <see cref="HealthBreakdown"/> of the four probe verdicts behind it. Observed, never inferred
/// (trust-boundaries.md §3); emitted only on an actual health transition, so a steady fleet is quiet.
/// </summary>
/// <param name="ServerId">The Server whose health changed (also on the envelope).</param>
/// <param name="Health">Its newly rolled-up health.</param>
/// <param name="Reason">A short, <b>untrusted</b> human-readable summary (trust-boundaries.md §8); non-secret.</param>
/// <param name="Breakdown">The four probe verdicts behind the rollup.</param>
[ProtocolMessage("server.health-changed")]
public sealed record HealthChanged(
    ServerId ServerId,
    ServerHealth Health,
    string Reason,
    HealthBreakdown Breakdown) : AgentEvent;
