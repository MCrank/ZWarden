using ZWarden.Application.Servers;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Infrastructure.Servers;

/// <summary>
/// Reconciles an Agent's observed snapshot against the ambient tenant's persisted Servers (F14). It records
/// the last-reported run-state on each Server whose id the Agent reported, and refreshes the discovery cache
/// with the full observed set. It never creates or deletes a Server, and never infers a transition for a
/// Server the Agent did not mention (trust-boundaries.md §3). An observed id with no matching Server is left
/// for the import picker; a persisted Server the Agent did not report is untouched.
/// </summary>
public sealed class ServerStateReconciler : IServerStateReconciler
{
    private readonly ZWardenDbContext _context;
    private readonly ServerRepository _servers;
    private readonly IServerDiscoveryCache _discovery;
    private readonly TimeProvider _clock;

    public ServerStateReconciler(
        ZWardenDbContext context,
        ServerRepository servers,
        IServerDiscoveryCache discovery,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(clock);
        _context = context;
        _servers = servers;
        _discovery = discovery;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task ReconcileAsync(
        AgentId agentId,
        IReadOnlyList<DiscoveredServer> observed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observed);

        IReadOnlyList<Server> persisted = await _servers.ListByAgentAsync(agentId, cancellationToken)
            .ConfigureAwait(false);
        Dictionary<ServerId, DiscoveredServer> observedById = observed
            .GroupBy(o => o.ServerId)
            .ToDictionary(g => g.Key, g => g.Last());

        DateTimeOffset now = _clock.GetUtcNow();
        bool changed = false;
        foreach (Server server in persisted)
        {
            if (observedById.TryGetValue(server.Id, out DiscoveredServer? report))
            {
                server.RecordObservedState(report.RunState, now);
                changed = true;
            }
        }

        if (changed)
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        _discovery.Record(agentId, observed);
    }
}
