using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Identity;

/// <summary>
/// The Agent's resolved self-identity (F8), available to the rest of the runtime once startup has
/// loaded or created it. This is the Agent's stable <c>agt-</c> id — not a credential and not proof
/// of trust; enrollment (F9) binds it to the control plane.
/// </summary>
public interface IAgentIdentity
{
    /// <summary>The Agent's stable id. Throws if accessed before startup has resolved it.</summary>
    AgentId AgentId { get; }
}
