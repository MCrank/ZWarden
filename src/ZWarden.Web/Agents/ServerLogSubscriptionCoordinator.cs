using Microsoft.AspNetCore.SignalR;
using ZWarden.Application.Agents;
using ZWarden.Contracts.Protocol;
using ZWarden.Domain.Ids;

namespace ZWarden.Web.Agents;

/// <summary>
/// The default <see cref="IServerLogSubscriptionCoordinator"/> (F27): a process-local viewer ref-count per Server
/// that starts and stops the owning Agent's follow through the <see cref="AgentHub"/> and the shared
/// <see cref="IAgentConnectionRegistry"/>. A singleton — the ref-counts are shared across every live circuit that
/// opens a log panel. Start/Stop ride the transport-level hub methods, not a command (ADR 0030): no OperationId,
/// no reply, no lock.
/// </summary>
public sealed partial class ServerLogSubscriptionCoordinator : IServerLogSubscriptionCoordinator
{
    private readonly IAgentConnectionRegistry _registry;
    private readonly IHubContext<AgentHub> _hub;
    private readonly ILogger<ServerLogSubscriptionCoordinator> _logger;
    private readonly Dictionary<ServerId, int> _viewerCounts = [];
    private readonly Lock _gate = new();

    public ServerLogSubscriptionCoordinator(
        IAgentConnectionRegistry registry,
        IHubContext<AgentHub> hub,
        ILogger<ServerLogSubscriptionCoordinator> logger)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(logger);
        _registry = registry;
        _hub = hub;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task SubscribeAsync(ServerId serverId, AgentId owningAgentId, CancellationToken cancellationToken)
    {
        bool firstViewer;
        lock (_gate)
        {
            int count = _viewerCounts.GetValueOrDefault(serverId);
            _viewerCounts[serverId] = count + 1;
            firstViewer = count == 0;
        }

        return firstViewer
            ? SendAsync(AgentHubProtocol.StartServerLogStream, serverId, owningAgentId, cancellationToken)
            : Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UnsubscribeAsync(ServerId serverId, AgentId owningAgentId, CancellationToken cancellationToken)
    {
        bool lastViewer;
        lock (_gate)
        {
            if (!_viewerCounts.TryGetValue(serverId, out int count) || count <= 0)
            {
                return Task.CompletedTask; // never subscribed, or already fully released
            }

            if (count == 1)
            {
                _viewerCounts.Remove(serverId);
                lastViewer = true;
            }
            else
            {
                _viewerCounts[serverId] = count - 1;
                lastViewer = false;
            }
        }

        return lastViewer
            ? SendAsync(AgentHubProtocol.StopServerLogStream, serverId, owningAgentId, cancellationToken)
            : Task.CompletedTask;
    }

    private async Task SendAsync(string method, ServerId serverId, AgentId owningAgentId, CancellationToken cancellationToken)
    {
        string? connectionId = _registry.GetConnectionId(owningAgentId);
        if (connectionId is null)
        {
            // The Agent is offline: nothing to drive. The panel simply shows no lines; reopening it re-subscribes.
            LogAgentOffline(owningAgentId, serverId);
            return;
        }

        await _hub.Clients.Client(connectionId).SendAsync(method, serverId.ToString(), cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Agent {AgentId} is offline; cannot drive the log stream for server {ServerId}.")]
    private partial void LogAgentOffline(AgentId agentId, ServerId serverId);
}
