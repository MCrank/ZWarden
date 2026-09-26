using System.Collections.Concurrent;
using ZWarden.Application.Servers;
using ZWarden.Domain.Ids;

namespace ZWarden.Infrastructure.Servers;

/// <summary>The default <see cref="IHostCapacityCache"/> (#230): a process-local singleton map of the newest report
/// per Agent, like <see cref="ServerMetricsCache"/>.</summary>
public sealed class HostCapacityCache : IHostCapacityCache
{
    private readonly ConcurrentDictionary<AgentId, HostCapacity> _latest = new();

    /// <inheritdoc />
    public void Record(HostCapacity capacity)
    {
        ArgumentNullException.ThrowIfNull(capacity);
        _latest[capacity.AgentId] = capacity;
    }

    /// <inheritdoc />
    public HostCapacity? GetLatest(AgentId agentId) => _latest.TryGetValue(agentId, out HostCapacity? capacity) ? capacity : null;
}
