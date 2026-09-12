using Microsoft.Extensions.Options;
using ZWarden.Application.Audit;
using ZWarden.Domain.Agents;
using ZWarden.Domain.Audit;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Infrastructure.Agents;

/// <summary>
/// Reconciles persisted connection state for Agents that stopped being heard from (F10) — the durable safety
/// net behind the live hub. It marks any Agent recorded <see cref="AgentConnectionState.Connected"/> whose
/// last-seen is older than <see cref="AgentConnectionMonitorOptions.StaleAfter"/> as disconnected, and audits
/// it. It only ever moves <c>Connected → Disconnected</c>: it never promotes a record back to current
/// (trust-boundaries.md §3), that requires a fresh connection. Runs under the default-tenant fallback, which
/// in single-tenant v1.0 covers every Agent (ADR 0016).
/// </summary>
public sealed class AgentConnectionSweeper
{
    private readonly ZWardenDbContext _context;
    private readonly AgentRepository _agents;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _clock;
    private readonly AgentConnectionMonitorOptions _options;

    public AgentConnectionSweeper(
        ZWardenDbContext context,
        AgentRepository agents,
        IAuditWriter audit,
        TimeProvider clock,
        IOptions<AgentConnectionMonitorOptions> options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);
        _context = context;
        _agents = agents;
        _audit = audit;
        _clock = clock;
        _options = options.Value;
    }

    /// <summary>Marks stale connections disconnected and audits each. Returns how many were reconciled.</summary>
    public async Task<int> SweepAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _clock.GetUtcNow();
        DateTimeOffset threshold = now - _options.StaleAfter;

        IReadOnlyList<Agent> stale = await _agents.FindStaleConnectionsAsync(threshold, cancellationToken)
            .ConfigureAwait(false);
        if (stale.Count == 0)
        {
            return 0;
        }

        foreach (Agent agent in stale)
        {
            agent.MarkDisconnected(now);
            await _audit.WriteAsync(
                new AuditEntry(
                    AgentConnectionAuditActions.Disconnected,
                    AuditOutcome.Succeeded,
                    null,
                    null,
                    $"agent {agent.Id}; stale (heartbeat lost)"),
                cancellationToken).ConfigureAwait(false);
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return stale.Count;
    }
}
