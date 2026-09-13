using System.Collections.Concurrent;
using ZWarden.Application.Servers;
using ZWarden.Domain.Ids;

namespace ZWarden.Infrastructure.Servers;

/// <summary>
/// The default <see cref="IServerHealthCache"/> (F16): a process-local map of the newest health rollup per
/// Server, a singleton beside <see cref="ServerMetricsCache"/>. <see cref="GetLatest"/> enforces the ownership
/// guard — a rollup is returned only when the caller names the Agent that reported it.
/// </summary>
public sealed class ServerHealthCache : IServerHealthCache
{
    private readonly ConcurrentDictionary<ServerId, ServerLiveHealth> _latest = new();

    /// <inheritdoc />
    public void Record(ServerLiveHealth health)
    {
        ArgumentNullException.ThrowIfNull(health);
        _latest[health.ServerId] = health;
    }

    /// <inheritdoc />
    public ServerLiveHealth? GetLatest(ServerId serverId, AgentId owningAgentId)
    {
        if (_latest.TryGetValue(serverId, out ServerLiveHealth? health) && health.AgentId == owningAgentId)
        {
            return health;
        }

        return null;
    }
}
