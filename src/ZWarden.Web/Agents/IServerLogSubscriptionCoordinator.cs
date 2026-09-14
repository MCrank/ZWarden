using ZWarden.Domain.Ids;

namespace ZWarden.Web.Agents;

/// <summary>
/// Ref-counts the live-log viewers of each Server and drives the Agent's on-demand follow accordingly (F27). The
/// interactive log panel subscribes when it opens and unsubscribes when it closes; the coordinator tells the
/// owning Agent to <c>StartServerLogStream</c> on the first viewer and <c>StopServerLogStream</c> on the last, so
/// a Server's logs stream only while someone is watching — the whole point of the on-demand model (ADR 0030). A
/// singleton, beside the connection registry it routes through.
/// </summary>
public interface IServerLogSubscriptionCoordinator
{
    /// <summary>Registers a viewer of <paramref name="serverId"/>'s logs; on the first viewer, asks
    /// <paramref name="owningAgentId"/> to begin following. A no-op toward the Agent when it is offline.</summary>
    Task SubscribeAsync(ServerId serverId, AgentId owningAgentId, CancellationToken cancellationToken);

    /// <summary>Removes a viewer of <paramref name="serverId"/>'s logs; on the last viewer, asks
    /// <paramref name="owningAgentId"/> to stop following.</summary>
    Task UnsubscribeAsync(ServerId serverId, AgentId owningAgentId, CancellationToken cancellationToken);
}
