using ZWarden.Domain.Ids;

namespace ZWarden.Application.Agents;

/// <summary>
/// #368: tells a connected Agent that the Agent ids it owns changed (the Owner replaced a Host with it), so it fetches
/// its inherited ids and re-sends its snapshot. Does nothing when the Agent is offline; it fetches them on connect.
/// </summary>
public interface IAgentOwnershipNotifier
{
    /// <summary>Sends the change to <paramref name="agentId"/> if it is connected.</summary>
    Task OwnershipChangedAsync(AgentId agentId, CancellationToken cancellationToken = default);
}
