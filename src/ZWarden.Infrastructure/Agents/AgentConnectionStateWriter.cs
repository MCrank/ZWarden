using ZWarden.Application.Agents;
using ZWarden.Domain.Agents;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Infrastructure.Agents;

/// <summary>
/// Persists an Agent's observed connection state (F10) via the tenant-scoped <see cref="AgentRepository"/>.
/// The connecting Agent has no browser session, so — like F9's enrollment exchange — these writes run under
/// the default-tenant fallback (ADR 0016) with no <c>IgnoreQueryFilters</c>. A stamp for an Agent not visible
/// in the ambient tenant is a no-op: the credential verifier has already resolved the Agent by the time the
/// hub stamps its state, so a miss here is defensive, not an expected path, and must never tear the connection.
/// </summary>
public sealed class AgentConnectionStateWriter : IAgentConnectionStateWriter
{
    private readonly ZWardenDbContext _context;
    private readonly AgentRepository _agents;
    private readonly TimeProvider _clock;

    public AgentConnectionStateWriter(ZWardenDbContext context, AgentRepository agents, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(clock);
        _context = context;
        _agents = agents;
        _clock = clock;
    }

    /// <inheritdoc />
    public Task MarkConnectedAsync(AgentId agentId, int protocolVersion, CancellationToken cancellationToken = default)
        => StampAsync(agentId, agent => agent.MarkConnected(protocolVersion, _clock.GetUtcNow()), cancellationToken);

    /// <inheritdoc />
    public Task MarkHeartbeatAsync(AgentId agentId, CancellationToken cancellationToken = default)
        => StampAsync(agentId, agent => agent.MarkHeartbeat(_clock.GetUtcNow()), cancellationToken);

    /// <inheritdoc />
    public Task MarkDisconnectedAsync(AgentId agentId, CancellationToken cancellationToken = default)
        => StampAsync(agentId, agent => agent.MarkDisconnected(_clock.GetUtcNow()), cancellationToken);

    private async Task StampAsync(AgentId agentId, Action<Agent> mutate, CancellationToken cancellationToken)
    {
        Agent? agent = await _agents.FindByIdAsync(agentId, cancellationToken).ConfigureAwait(false);
        if (agent is null)
        {
            return;
        }

        mutate(agent);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
