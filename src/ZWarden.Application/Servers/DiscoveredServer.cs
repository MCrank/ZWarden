using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;

namespace ZWarden.Application.Servers;

/// <summary>
/// One canonical container an Agent reported in its last state snapshot (F14, trust-boundaries.md §3):
/// the <see cref="ServerId"/> it carries (its <c>io.zwarden.server-id</c> label) and its observed
/// run-state. This is <b>untrusted, observed</b> data — the input to reconciliation and to the import
/// picker (a discovered container with no Server record is an orphan an operator may adopt).
/// </summary>
public sealed record DiscoveredServer(ServerId ServerId, ServerRunState RunState);
