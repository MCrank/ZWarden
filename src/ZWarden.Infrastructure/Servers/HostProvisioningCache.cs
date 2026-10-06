using System.Collections.Concurrent;
using ZWarden.Application.Servers;
using ZWarden.Domain.Ids;

namespace ZWarden.Infrastructure.Servers;

/// <summary>The default <see cref="IHostProvisioningCache"/> (#364): a process-local singleton map, like
/// <see cref="HostCapacityCache"/>.</summary>
public sealed class HostProvisioningCache : IHostProvisioningCache
{
    private readonly ConcurrentDictionary<AgentId, bool> _pzImageReady = new();

    /// <inheritdoc />
    public void Record(AgentId agentId, bool pzImageReady) => _pzImageReady[agentId] = pzImageReady;

    /// <inheritdoc />
    public bool? IsPzImageReady(AgentId agentId) => _pzImageReady.TryGetValue(agentId, out bool ready) ? ready : null;
}
