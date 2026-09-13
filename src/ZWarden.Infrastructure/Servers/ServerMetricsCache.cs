using System.Collections.Concurrent;
using ZWarden.Application.Servers;
using ZWarden.Domain.Ids;

namespace ZWarden.Infrastructure.Servers;

/// <summary>
/// The default <see cref="IServerMetricsCache"/> (F16): a process-local <see cref="ConcurrentDictionary{TKey,TValue}"/>
/// of the newest sample per Server. A singleton, like <see cref="ServerDiscoveryCache"/>. <see cref="Get"/>
/// enforces the ownership guard — it returns a sample only when the caller names the Agent that reported it.
/// </summary>
public sealed class ServerMetricsCache : IServerMetricsCache
{
    private readonly ConcurrentDictionary<ServerId, ServerMetrics> _latest = new();

    /// <inheritdoc />
    public void Record(IReadOnlyList<ServerMetrics> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        foreach (ServerMetrics sample in samples)
        {
            _latest[sample.ServerId] = sample;
        }
    }

    /// <inheritdoc />
    public ServerMetrics? GetLatest(ServerId serverId, AgentId owningAgentId)
    {
        if (_latest.TryGetValue(serverId, out ServerMetrics? sample) && sample.AgentId == owningAgentId)
        {
            return sample;
        }

        return null;
    }
}
