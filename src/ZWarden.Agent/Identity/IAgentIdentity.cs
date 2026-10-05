using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Identity;

/// <summary>
/// The Agent's resolved identity, available to the rest of the runtime once startup has loaded it. The Agent has
/// one <b>operational</b> id (#365): the AgentId the control plane assigned at enrollment (F9, shown on the Hosts
/// card) once it is known, and until then the local id from the F8 identity file. The local id is not a credential
/// and proves nothing; it is kept because containers created before #365 are stamped with it.
/// </summary>
public interface IAgentIdentity
{
    /// <summary>
    /// The operational id: the enrolled AgentId once enrolled, otherwise <see cref="LocalId"/>. New containers,
    /// logs and diagnostics carry it. Throws if accessed before startup has resolved the identity.
    /// </summary>
    AgentId AgentId { get; }

    /// <summary>The local id from the F8 identity file. Throws if accessed before startup has resolved it.</summary>
    AgentId LocalId { get; }

    /// <summary>
    /// Whether a container stamped with <paramref name="agentId"/> belongs to this Agent: the enrolled id or the
    /// local id (Docker labels can't be changed in place, so a container created under the local id stays owned
    /// until a Recreate re-stamps it).
    /// </summary>
    bool Owns(AgentId agentId);
}
