using System.Collections.Concurrent;
using ZWarden.Application.Servers;
using ZWarden.Domain.Ids;

namespace ZWarden.Infrastructure.Servers;

/// <summary>
/// The process-local, per-Agent discovery cache (F14) — the "as of the last snapshot" view of each Agent's
/// canonical containers, alongside the F10 connection registry. Not persisted: it is rebuilt from the next
/// snapshot after a restart, and a Server that matters is a persisted <see cref="Domain.Servers.Server"/>,
/// not a cache entry. Registered as a singleton.
/// </summary>
public sealed class ServerDiscoveryCache : IServerDiscoveryCache
{
    private readonly ConcurrentDictionary<AgentId, IReadOnlyList<DiscoveredServer>> _byAgent = new();

    /// <inheritdoc />
    public void Record(AgentId agentId, IReadOnlyList<DiscoveredServer> servers)
    {
        ArgumentNullException.ThrowIfNull(servers);
        _byAgent[agentId] = [.. servers];
    }

    /// <inheritdoc />
    public IReadOnlyList<DiscoveredServer> GetDiscovered(AgentId agentId)
        => _byAgent.TryGetValue(agentId, out IReadOnlyList<DiscoveredServer>? servers) ? servers : [];
}
