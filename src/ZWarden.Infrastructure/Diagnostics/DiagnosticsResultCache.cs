using System.Collections.Concurrent;
using ZWarden.Application.Diagnostics;
using ZWarden.Domain.Ids;

namespace ZWarden.Infrastructure.Diagnostics;

/// <summary>
/// The default <see cref="IDiagnosticsResultCache"/> (F29): process-local maps of the newest gather bundle per
/// Agent (host domains) and per Server (server domains), a singleton beside the F16/F19 caches. Server reads
/// enforce the ownership guard — a bundle is returned only when the caller names the Agent that reported it
/// (trust-boundaries §8) — so a bundle recorded for an unguessable ServerId cannot surface under the wrong Server.
/// </summary>
public sealed class DiagnosticsResultCache : IDiagnosticsResultCache
{
    private readonly ConcurrentDictionary<AgentId, DiagnosticBundle> _host = new();
    private readonly ConcurrentDictionary<ServerId, OwnedBundle> _server = new();

    /// <inheritdoc />
    public void RecordHost(AgentId agentId, DiagnosticBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        _host[agentId] = bundle;
    }

    /// <inheritdoc />
    public DiagnosticBundle? GetHost(AgentId agentId) =>
        _host.TryGetValue(agentId, out DiagnosticBundle? bundle) ? bundle : null;

    /// <inheritdoc />
    public void RecordServer(ServerId serverId, AgentId owningAgentId, DiagnosticBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        _server[serverId] = new OwnedBundle(owningAgentId, bundle);
    }

    /// <inheritdoc />
    public DiagnosticBundle? GetServer(ServerId serverId, AgentId owningAgentId)
    {
        if (_server.TryGetValue(serverId, out OwnedBundle owned) && owned.OwningAgentId == owningAgentId)
        {
            return owned.Bundle;
        }

        return null;
    }

    private readonly record struct OwnedBundle(AgentId OwningAgentId, DiagnosticBundle Bundle);
}
