using ZWarden.Application.Agents;
using ZWarden.Application.Diagnostics;
using ZWarden.Domain.Agents;
using ZWarden.Infrastructure.Agents;

namespace ZWarden.Infrastructure.Diagnostics;

/// <summary>
/// Gathers the enrolled Agents' <see cref="AgentPresenceFacts"/> (F29): the tenant's Agents from the
/// tenant-scoped <see cref="AgentRepository"/> (ADR 0016) combined with the live per-process connection registry
/// (F10). <see cref="AgentPresenceFacts.IsConnected"/> is the registry's "connected now" truth; the durable
/// <c>LastSeenAt</c> is the companion the pure rollup (<c>AgentConnectivityDiagnostic</c>) uses to tell a
/// transient reconnect from an offline Agent.
/// </summary>
public sealed class DiagnosticsAgentPresenceProbe : IDiagnosticsAgentPresenceProbe
{
    private readonly AgentRepository _agents;
    private readonly IAgentConnectionRegistry _registry;

    public DiagnosticsAgentPresenceProbe(AgentRepository agents, IAgentConnectionRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(registry);
        _agents = agents;
        _registry = registry;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentPresenceFacts>> ProbeAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Agent> all = await _agents.ListAsync(cancellationToken).ConfigureAwait(false);
        return all
            .Select(a => new AgentPresenceFacts(
                a.Id,
                a.Label,
                a.IsEnabled,
                _registry.IsConnected(a.Id),
                a.LastSeenAt))
            .ToList();
    }
}
