using System.Collections.Concurrent;
using ZWarden.Application.Mods;
using ZWarden.Domain.Ids;

namespace ZWarden.Infrastructure.Mods;

/// <summary>
/// The default <see cref="IModInventoryCache"/> (F21): a process-local <see cref="ConcurrentDictionary{TKey,TValue}"/>
/// of the newest inventory per Server. A singleton, like <c>PlayerRosterCache</c> and <c>ServerMetricsCache</c>.
/// <see cref="GetLatest"/> enforces the ownership guard — it returns an inventory only when the caller names the
/// Agent that reported it.
/// </summary>
public sealed class ModInventoryCache : IModInventoryCache
{
    private readonly ConcurrentDictionary<ServerId, ModInventory> _latest = new();

    /// <inheritdoc />
    public void Record(ModInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        _latest[inventory.ServerId] = inventory;
    }

    /// <inheritdoc />
    public ModInventory? GetLatest(ServerId serverId, AgentId owningAgentId)
    {
        if (_latest.TryGetValue(serverId, out ModInventory? inventory) && inventory.AgentId == owningAgentId)
        {
            return inventory;
        }

        return null;
    }
}
