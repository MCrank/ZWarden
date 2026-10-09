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
/// <param name="Foreign">#368: canonical PZ containers on the Host stamped with an id this Agent doesn't own (a machine
/// wiped and enrolled again), at most <see cref="MaxForeignContainers"/>. Additive and optional (ADR 0020):
/// <c>null</c> from an Agent that predates #368. Observed and untrusted: ZWarden uses it only to offer and re-check a
/// Replace host, never as an authorization on its own (trust-boundaries.md §3).</param>
[ProtocolMessage("agent.state-snapshot")]
public sealed record AgentStateSnapshot(
    IReadOnlyList<ServerState> Servers,
    IReadOnlyList<ForeignContainer>? Foreign = null) : AgentEvent
{
    /// <summary>The most foreign containers one snapshot reports (#368).</summary>
    public const int MaxForeignContainers = 64;

    /// <summary>An empty snapshot — the Agent manages no Servers.</summary>
    public static AgentStateSnapshot Empty { get; } = new(ReadOnlyCollection<ServerState>.Empty);
}

/// <summary>One Server's observed state within an <see cref="AgentStateSnapshot"/>.</summary>
/// <param name="ServerId">The Server.</param>
/// <param name="RunState">Its observed run-state.</param>
/// <param name="Health">Its observed hierarchical health rollup (F16). Additive and optional (ADR 0020):
/// <c>null</c> from an Agent that predates F16 or has not yet computed a rollup — the reconciler leaves the
/// persisted health untouched in that case.</param>
public sealed record ServerState(ServerId ServerId, ServerRunState RunState, ServerHealth? Health = null);

/// <summary>
/// #368: a canonical PZ container the Agent found on its Host but doesn't own, because it is stamped with another
/// Agent's id. The Agent never operates on it; it only reports it.
/// </summary>
/// <param name="ContainerId">The Docker short id (12 characters), for the operator's <c>docker rm</c>.</param>
/// <param name="ServerId">The Server it hosts (its <c>io.zwarden.server-id</c> label).</param>
/// <param name="LabelledAgentId">The Agent id it is stamped with (its <c>io.zwarden.agent-id</c> label).</param>
/// <param name="State">The Docker state (<c>running</c>, <c>exited</c>, ...), at most <see cref="MaxStateLength"/>.</param>
public sealed record ForeignContainer(string ContainerId, ServerId ServerId, AgentId LabelledAgentId, string State)
{
    /// <summary>The longest state string reported.</summary>
    public const int MaxStateLength = 32;
}
