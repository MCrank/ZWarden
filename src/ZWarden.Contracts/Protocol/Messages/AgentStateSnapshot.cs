using System.Collections.ObjectModel;
using ZWarden.Domain.Ids;

namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// The authoritative snapshot of what the Agent observes, sent after (re)connect (PRD 40,
/// trust-boundaries.md §3). It reports the observed run-state of every Server the Agent manages, so
/// ZWarden.Web can reconcile desired against observed without inferring any transition it was not
/// told about.
/// </summary>
/// <param name="Servers">The observed run-state of each Server the Agent manages.</param>
[ProtocolMessage("agent.state-snapshot")]
public sealed record AgentStateSnapshot(IReadOnlyList<ServerState> Servers) : AgentEvent
{
    /// <summary>An empty snapshot — the Agent manages no Servers.</summary>
    public static AgentStateSnapshot Empty { get; } = new(ReadOnlyCollection<ServerState>.Empty);
}

/// <summary>One Server's observed run-state within an <see cref="AgentStateSnapshot"/>.</summary>
/// <param name="ServerId">The Server.</param>
/// <param name="RunState">Its observed run-state.</param>
public sealed record ServerState(ServerId ServerId, ServerRunState RunState);
