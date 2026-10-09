using Microsoft.AspNetCore.SignalR;
using ZWarden.Application.Agents;
using ZWarden.Contracts.Protocol;
using ZWarden.Domain.Ids;

namespace ZWarden.Web.Agents;

/// <summary>
/// The SignalR <see cref="IAgentOwnershipNotifier"/> (#368): after a Replace host, tells the successor's Agent, when it
/// is connected to this Web process, to fetch its inherited ids and re-send its snapshot. An offline Agent fetches them
/// when it next connects, so there is nothing to queue.
/// </summary>
public sealed class AgentOwnershipNotifier : IAgentOwnershipNotifier
{
    private readonly IHubContext<AgentHub> _hub;
    private readonly IAgentConnectionRegistry _registry;

    public AgentOwnershipNotifier(IHubContext<AgentHub> hub, IAgentConnectionRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(registry);
        _hub = hub;
        _registry = registry;
    }

    /// <inheritdoc />
    public Task OwnershipChangedAsync(AgentId agentId, CancellationToken cancellationToken = default)
        => _registry.GetConnectionId(agentId) is { } connectionId
            ? _hub.Clients.Client(connectionId).SendAsync(AgentHubProtocol.OwnershipChanged, cancellationToken)
            : Task.CompletedTask;
}
