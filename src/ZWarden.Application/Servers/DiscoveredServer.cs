using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;

namespace ZWarden.Application.Servers;

/// <summary>
/// One canonical container an Agent reported in its last state snapshot (F14, trust-boundaries.md §3):
/// the <see cref="ServerId"/> it carries (its <c>io.zwarden.server-id</c> label), its observed run-state, and
/// (F16) its observed hierarchical <see cref="ServerHealth"/> rollup when the Agent computed one. This is
/// <b>untrusted, observed</b> data — the input to reconciliation and to the import picker (a discovered
/// container with no Server record is an orphan an operator may adopt).
/// </summary>
/// <param name="ServerId">The Server the container carries.</param>
/// <param name="RunState">Its observed run-state.</param>
/// <param name="Health">Its observed health rollup, or <c>null</c> when the Agent reported none (pre-F16 Agent,
/// or not yet computed) — the reconciler then leaves the persisted health untouched.</param>
public sealed record DiscoveredServer(ServerId ServerId, ServerRunState RunState, ServerHealth? Health = null);
