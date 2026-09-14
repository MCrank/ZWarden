using System.Collections.Concurrent;
using ZWarden.Application.Players;
using ZWarden.Domain.Ids;

namespace ZWarden.Infrastructure.Players;

/// <summary>
/// The default <see cref="IPlayerRosterCache"/> (F19): a process-local <see cref="ConcurrentDictionary{TKey,TValue}"/>
/// of the newest roster per Server. A singleton, like <c>ServerMetricsCache</c>. <see cref="GetLatest"/> enforces
/// the ownership guard — it returns a roster only when the caller names the Agent that reported it.
/// </summary>
public sealed class PlayerRosterCache : IPlayerRosterCache
{
    private readonly ConcurrentDictionary<ServerId, PlayerRoster> _latest = new();

    /// <inheritdoc />
    public void Record(PlayerRoster roster)
    {
        ArgumentNullException.ThrowIfNull(roster);
        _latest[roster.ServerId] = roster;
    }

    /// <inheritdoc />
    public PlayerRoster? GetLatest(ServerId serverId, AgentId owningAgentId)
    {
        if (_latest.TryGetValue(serverId, out PlayerRoster? roster) && roster.AgentId == owningAgentId)
        {
            return roster;
        }

        return null;
    }
}
