using ZWarden.Application.Audit;
using ZWarden.Application.Authorization;
using ZWarden.Application.Operations;
using ZWarden.Application.Servers;
using ZWarden.Domain.Agents;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Agents;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Infrastructure.Servers;

/// <summary>
/// The tenant-scoped inventory + import service (F14). Reads are authorized per Server and fail-closed
/// (ADR 0018): a tenant-wide <c>Server.View</c> grant sees all, otherwise each Server is filtered by a
/// server-scoped check. Import re-checks <c>Server.Register</c> (tenant-wide), confirms the Agent exists in
/// the tenant, and confirms the target was actually discovered — so a forged or stale request cannot adopt
/// an arbitrary id — then records an audit event. Every read passes the tenant filter (ADR 0016).
/// </summary>
public sealed class ServerInventory : IServerInventory
{
    private readonly ZWardenDbContext _context;
    private readonly ServerRepository _servers;
    private readonly AgentRepository _agents;
    private readonly IServerDiscoveryCache _discovery;
    private readonly IPermissionChecker _permissions;
    private readonly IOperationCoordinator _operations;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _clock;

    public ServerInventory(
        ZWardenDbContext context,
        ServerRepository servers,
        AgentRepository agents,
        IServerDiscoveryCache discovery,
        IPermissionChecker permissions,
        IOperationCoordinator operations,
        IAuditWriter audit,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(clock);
        _context = context;
        _servers = servers;
        _agents = agents;
        _discovery = discovery;
        _permissions = permissions;
        _operations = operations;
        _audit = audit;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ServerSummary>> ListVisibleAsync(
        UserId user,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Server> all = await _servers.ListAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlySet<string> tenantWide = await _permissions
            .GetTenantWidePermissionsAsync(user, cancellationToken).ConfigureAwait(false);

        List<Server> visible;
        if (tenantWide.Contains(Permissions.ServerView.Name))
        {
            visible = [.. all];
        }
        else
        {
            visible = [];
            foreach (Server server in all)
            {
                AuthorizationDecision decision = await _permissions
                    .EvaluateAsync(user, Permissions.ServerView, server.Id, cancellationToken).ConfigureAwait(false);
                if (decision.IsAllowed)
                {
                    visible.Add(server);
                }
            }
        }

        return visible
            .OrderByDescending(s => s.CreatedAt)
            .Select(ToSummary)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<ServerSummary?> GetVisibleAsync(
        UserId user,
        ServerId server,
        CancellationToken cancellationToken = default)
    {
        Server? found = await _servers.FindByIdAsync(server, cancellationToken).ConfigureAwait(false);
        if (found is null)
        {
            return null;
        }

        AuthorizationDecision decision = await _permissions
            .EvaluateAsync(user, Permissions.ServerView, found.Id, cancellationToken).ConfigureAwait(false);
        return decision.IsAllowed ? ToSummary(found) : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DiscoveredServer>> ListDiscoveredUnregisteredAsync(
        AgentId agentId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Server> registered = await _servers.ListByAgentAsync(agentId, cancellationToken)
            .ConfigureAwait(false);
        HashSet<ServerId> registeredIds = [.. registered.Select(s => s.Id)];
        return _discovery.GetDiscovered(agentId)
            .Where(d => !registeredIds.Contains(d.ServerId))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DiscoveredServerOnAgent>> ListAllDiscoveredUnregisteredAsync(
        CancellationToken cancellationToken = default)
    {
        HashSet<ServerId> registeredIds = [.. (await _servers.ListAsync(cancellationToken).ConfigureAwait(false))
            .Select(s => s.Id)];
        return _discovery.KnownAgents()
            .SelectMany(agentId => _discovery.GetDiscovered(agentId)
                .Where(d => !registeredIds.Contains(d.ServerId))
                .Select(d => new DiscoveredServerOnAgent(agentId, d.ServerId, d.RunState)))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<ServerImportResult> ImportAsync(
        UserId user,
        AgentId agentId,
        ServerId serverId,
        string name,
        CancellationToken cancellationToken = default)
    {
        AuthorizationDecision decision = await _permissions
            .EvaluateAsync(user, Permissions.ServerRegister, server: null, cancellationToken).ConfigureAwait(false);
        if (!decision.IsAllowed)
        {
            return ServerImportResult.Denied(ServerImportFailure.NotAuthorized);
        }

        Agent? agent = await _agents.FindByIdAsync(agentId, cancellationToken).ConfigureAwait(false);
        if (agent is null)
        {
            return ServerImportResult.Denied(ServerImportFailure.AgentNotFound);
        }

        // The target must be something this Agent actually discovered (trust-boundaries.md §3): a caller
        // cannot register an arbitrary id, only adopt an observed orphan.
        if (!_discovery.GetDiscovered(agentId).Any(d => d.ServerId == serverId))
        {
            return ServerImportResult.Denied(ServerImportFailure.NotDiscovered);
        }

        // Idempotent: re-importing an already-registered Server is a success, not a duplicate.
        Server? existing = await _servers.FindByIdAsync(serverId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return ServerImportResult.Success(existing.Id);
        }

        Server server = Server.Import(agentId, serverId, name, _clock.GetUtcNow());
        _servers.Add(server);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _audit.WriteAsync(
            new AuditEntry(ServerAuditActions.Imported, AuditOutcome.Succeeded, user, server.Id, $"agent {agentId}"),
            cancellationToken).ConfigureAwait(false);

        return ServerImportResult.Success(server.Id);
    }

    /// <inheritdoc />
    public async Task<ServerRegisterResult> RegisterAsync(
        UserId user,
        AgentId agentId,
        string name,
        CancellationToken cancellationToken = default)
    {
        AuthorizationDecision decision = await _permissions
            .EvaluateAsync(user, Permissions.ServerRegister, server: null, cancellationToken).ConfigureAwait(false);
        if (!decision.IsAllowed)
        {
            return ServerRegisterResult.Denied(ServerRegisterFailure.NotAuthorized);
        }

        Agent? agent = await _agents.FindByIdAsync(agentId, cancellationToken).ConfigureAwait(false);
        if (agent is null)
        {
            return ServerRegisterResult.Denied(ServerRegisterFailure.AgentNotFound);
        }

        Server server = Server.Register(agentId, name, _clock.GetUtcNow());
        _servers.Add(server);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _audit.WriteAsync(
            new AuditEntry(ServerAuditActions.Registered, AuditOutcome.Succeeded, user, server.Id, $"agent {agentId}"),
            cancellationToken).ConfigureAwait(false);

        // Enqueue the mutating, server-scoped provisioning Operation that creates the container (ADR 0022).
        Operation operation = await _operations.EnqueueAsync(
            new EnqueueOperationRequest(
                agentId,
                OperationKind.ProvisionServer,
                IsMutating: true,
                Guid.NewGuid().ToString("N"),
                ServerId: server.Id),
            user,
            cancellationToken).ConfigureAwait(false);

        return ServerRegisterResult.Success(server.Id, operation.Id);
    }

    private static ServerSummary ToSummary(Server server) => new(
        server.Id,
        server.AgentId,
        server.Name,
        server.Description,
        server.GamePort,
        server.QueryPort,
        server.LastRunState,
        server.LastStateReportedAt,
        server.LastHealth,
        server.LastHealthReportedAt,
        server.InstalledBuildId);
}
