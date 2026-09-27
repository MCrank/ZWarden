using Microsoft.EntityFrameworkCore;
using ZWarden.Application.Servers;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Operations;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Infrastructure.Servers;

/// <inheritdoc />
public sealed class ServerRemoval : IServerRemoval
{
    private readonly ZWardenDbContext _context;
    private readonly ServerRepository _servers;
    private readonly OperationRepository _operations;
    private readonly IServerDiscoveryCache _discovery;

    public ServerRemoval(
        ZWardenDbContext context,
        ServerRepository servers,
        OperationRepository operations,
        IServerDiscoveryCache discovery)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(discovery);
        _context = context;
        _servers = servers;
        _operations = operations;
        _discovery = discovery;
    }

    /// <inheritdoc />
    public async Task<bool> RecordDeletedAsync(
        OperationId operationId, AgentId reportingAgent, CancellationToken cancellationToken = default)
    {
        Operation? operation = await _operations.FindByIdAsync(operationId, cancellationToken).ConfigureAwait(false);
        if (operation is not { Kind: OperationKind.DeleteServer, ServerId: { } serverId })
        {
            return false;
        }

        Server? server = await _servers.FindByIdAsync(serverId, cancellationToken).ConfigureAwait(false);
        if (server is null || server.AgentId != reportingAgent || operation.AgentId != reportingAgent)
        {
            return false; // Already removed (a redelivered completion), or not this Agent's Server: nothing to do.
        }

        // Server-scoped grants would otherwise dangle on a ServerId that no longer exists (no FKs, D1).
        List<RoleAssignment> grants = await _context.Set<RoleAssignment>()
            .Where(a => a.ServerId == serverId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        _context.RemoveRange(grants);
        _context.Remove(server);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _discovery.Forget(reportingAgent, serverId);
        return true;
    }
}
