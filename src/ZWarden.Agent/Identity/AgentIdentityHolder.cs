using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Identity;

/// <summary>
/// The single mutable slot that holds the Agent's resolved identity for the process lifetime (F8, #365). The local
/// id is set once at startup by <see cref="AgentIdentityInitializer"/>; the enrolled id is recorded by the
/// enrollment step, which may finish after startup (a background retry), so both live in one immutable snapshot
/// that is replaced as a whole. Read everywhere else through <see cref="IAgentIdentity"/>.
/// </summary>
public sealed class AgentIdentityHolder : IAgentIdentity
{
    private readonly Lock _gate = new();
    private Ids _ids = new(null, null);

    /// <inheritdoc />
    public AgentId AgentId => Current.Enrolled ?? LocalId;

    /// <inheritdoc />
    public AgentId LocalId =>
        Current.Local ?? throw new InvalidOperationException(
            "The Agent identity has not been initialized yet. It is resolved at startup before the worker runs.");

    /// <summary>Whether the local identity has been resolved.</summary>
    public bool IsInitialized => Current.Local is not null;

    private Ids Current => Volatile.Read(ref _ids);

    /// <inheritdoc />
    public bool Owns(AgentId agentId)
    {
        Ids ids = Current;
        return agentId == ids.Enrolled || agentId == ids.Local;
    }

    /// <summary>Records the local identity. Called once, at startup.</summary>
    public void Set(AgentId agentId)
    {
        lock (_gate)
        {
            Volatile.Write(ref _ids, _ids with { Local = agentId });
        }
    }

    /// <summary>Records the AgentId the control plane assigned at enrollment; it becomes the operational id.</summary>
    public void MarkEnrolled(AgentId agentId)
    {
        lock (_gate)
        {
            Volatile.Write(ref _ids, _ids with { Enrolled = agentId });
        }
    }

    private sealed record Ids(AgentId? Local, AgentId? Enrolled);
}
