using System.Collections.Concurrent;
using ZWarden.Application.Agents;
using ZWarden.Domain.Ids;

namespace ZWarden.Web.Agents;

/// <summary>
/// The in-memory, per-process <see cref="IAgentConnectionRegistry"/> (F10, decision 2) — a singleton holding
/// the live SignalR connections this ZWarden.Web process owns. It maps each Agent to its current connection
/// (and the action that aborts it), so F11 can address a command to a connected Agent and F9 revoke/disable
/// can drop one immediately. It is authoritative only for <b>this</b> process; v1.0 runs a single Web
/// instance (ADR 0005), and a multi-instance deployment (F10A/v1.1) replaces it with a backplane-backed
/// implementation behind the same interface.
/// </summary>
public sealed class AgentConnectionRegistry : IAgentConnectionRegistry
{
    private sealed record Entry(AgentId AgentId, string ConnectionId, Action Abort);

    private readonly ConcurrentDictionary<string, Entry> _byConnectionId = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<AgentId, Entry> _byAgentId = new();

    /// <inheritdoc />
    public void Register(AgentId agentId, string connectionId, Action abort)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionId);
        ArgumentNullException.ThrowIfNull(abort);

        Entry entry = new(agentId, connectionId, abort);

        // A lingering socket from before a reconnect: displace and abort it so one Agent has one connection.
        if (_byAgentId.TryGetValue(agentId, out Entry? prior) && !string.Equals(prior.ConnectionId, connectionId, StringComparison.Ordinal))
        {
            _byConnectionId.TryRemove(prior.ConnectionId, out _);
            prior.Abort();
        }

        _byAgentId[agentId] = entry;
        _byConnectionId[connectionId] = entry;
    }

    /// <inheritdoc />
    public void Remove(string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId) || !_byConnectionId.TryRemove(connectionId, out Entry? entry))
        {
            return;
        }

        // Only clear the Agent mapping if it still points at the connection we removed (a newer connect wins).
        _byAgentId.TryRemove(KeyValuePair.Create(entry.AgentId, entry));
    }

    /// <inheritdoc />
    public bool IsConnected(AgentId agentId) => _byAgentId.ContainsKey(agentId);

    /// <inheritdoc />
    public string? GetConnectionId(AgentId agentId)
        => _byAgentId.TryGetValue(agentId, out Entry? entry) ? entry.ConnectionId : null;

    /// <inheritdoc />
    public bool TryAbort(AgentId agentId)
    {
        if (!_byAgentId.TryRemove(agentId, out Entry? entry))
        {
            return false;
        }

        _byConnectionId.TryRemove(entry.ConnectionId, out _);
        entry.Abort();
        return true;
    }
}
