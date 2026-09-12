using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Identity;

/// <summary>
/// The single mutable slot that holds the Agent's resolved identity for the process lifetime (F8).
/// Populated once at startup by <see cref="AgentIdentityInitializer"/>; read everywhere else through
/// <see cref="IAgentIdentity"/>.
/// </summary>
public sealed class AgentIdentityHolder : IAgentIdentity
{
    private AgentId? _agentId;

    /// <inheritdoc />
    public AgentId AgentId =>
        _agentId ?? throw new InvalidOperationException(
            "The Agent identity has not been initialized yet. It is resolved at startup before the worker runs.");

    /// <summary>Whether the identity has been resolved.</summary>
    public bool IsInitialized => _agentId is not null;

    /// <summary>Records the resolved identity. Called once, at startup.</summary>
    public void Set(AgentId agentId) => _agentId = agentId;
}
