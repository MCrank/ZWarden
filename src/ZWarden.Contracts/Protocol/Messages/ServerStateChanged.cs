using ZWarden.Domain.Ids;

namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// The Agent reports that a Server's observed <b>run-state</b> changed (F16; reserved on <see cref="AgentEvent"/>).
/// It is the incremental companion to <see cref="AgentStateSnapshot"/> — the snapshot is the authoritative set at
/// (re)connect, this is a single transition between snapshots — so ZWarden.Web reconciles observed state promptly
/// without inferring it from a command's success (trust-boundaries.md §3). Emitted only on an actual transition.
/// </summary>
/// <param name="ServerId">The Server whose run-state changed (also on the envelope).</param>
/// <param name="RunState">Its newly observed run-state.</param>
[ProtocolMessage("server.state-changed")]
public sealed record ServerStateChanged(ServerId ServerId, ServerRunState RunState) : AgentEvent;
