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
                if (report.Health is { } health)
                {
                    server.RecordObservedHealth(health, now);
                }

                changed = true;
            }
        }

        if (changed)
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        _discovery.Record(agentId, observed);
    }

    /// <inheritdoc />
    public Task RecordObservedStateAsync(
        AgentId agentId,
        ServerId serverId,
        ServerRunState runState,
        CancellationToken cancellationToken = default)
        => RecordOwnedAsync(
            agentId, serverId, (server, now) => server.RecordObservedState(runState, now), cancellationToken);

    /// <inheritdoc />
    public Task RecordObservedHealthAsync(
        AgentId agentId,
        ServerId serverId,
        ServerHealth health,
        CancellationToken cancellationToken = default)
        => RecordOwnedAsync(
            agentId, serverId, (server, now) => server.RecordObservedHealth(health, now), cancellationToken);

    // Applies an observed single-Server transition, but only to a Server this tenant owns AND the reporting
    // Agent owns — an Agent may not move the state of another Agent's Server (trust-boundaries.md §3/§8).
    private async Task RecordOwnedAsync(
        AgentId agentId,
        ServerId serverId,
        Action<Server, DateTimeOffset> apply,
        CancellationToken cancellationToken)
    {
        Server? server = await _servers.FindByIdAsync(serverId, cancellationToken).ConfigureAwait(false);
        if (server is null || server.AgentId != agentId)
        {
            return;
        }

        apply(server, _clock.GetUtcNow());
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RecordProvisionedAsync(
        ServerId serverId,
        int gamePort,
        int queryPort,
        string containerId,
        CancellationToken cancellationToken = default)
    {
        Server? server = await _servers.FindByIdAsync(serverId, cancellationToken).ConfigureAwait(false);
        if (server is null)
        {
            // A report for a Server this tenant does not own (or that has been removed): no-op.
            return;
        }

        server.RecordContainer(containerId, gamePort, queryPort);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RecordInstalledBuildAsync(
        ServerId serverId,
        string? buildId,
        CancellationToken cancellationToken = default)
    {
        Server? server = await _servers.FindByIdAsync(serverId, cancellationToken).ConfigureAwait(false);
        if (server is null)
        {
            // A report for a Server this tenant does not own (or that has been removed): no-op.
            return;
        }

        server.RecordObservedBuild(buildId, _clock.GetUtcNow());
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
