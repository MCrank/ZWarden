using ZWarden.Application.Authorization;
using ZWarden.Application.Mods;
using ZWarden.Application.Operations;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Servers;

namespace ZWarden.Infrastructure.Mods;

/// <summary>
/// The tenant-scoped Workshop-and-mod discovery service (F21) — a read-only sibling of <c>ServerDiagnostics</c> and
/// the roster path of <c>PlayerManagement</c>. It is <b>fail-closed</b> (ADR 0018): it resolves the Server through
/// the tenant filter (a foreign/unknown Server is <see cref="ModDiscoveryFailure.ServerNotFound"/>), authorizes the
/// server-scoped <c>Mod.View</c> against that specific Server, then enqueues a <b>non-mutating</b>
/// <see cref="OperationKind.ModDiscovery"/> on the Server's Agent — so it never claims the per-server lock
/// (ADR 0022). Being a read, it is not audited. The Agent reports the observed inventory on completion into the
/// in-memory <see cref="IModInventoryCache"/>.
/// </summary>
public sealed class ModDiscoveryService : IModDiscoveryService
{
    private readonly ServerRepository _servers;
    private readonly IPermissionChecker _permissions;
    private readonly IOperationCoordinator _operations;

    public ModDiscoveryService(
        ServerRepository servers,
        IPermissionChecker permissions,
        IOperationCoordinator operations)
    {
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(operations);
        _servers = servers;
        _permissions = permissions;
        _operations = operations;
    }

    /// <inheritdoc />
    public async Task<ModDiscoveryDispatch> RequestAsync(UserId user, ServerId server, CancellationToken cancellationToken = default)
    {
        // Resolve first, through the tenant filter: an unknown or foreign-tenant Server is ServerNotFound, and
        // gives the server-scoped authorization a concrete resource to check.
        Server? resolved = await _servers.FindByIdAsync(server, cancellationToken).ConfigureAwait(false);
        if (resolved is null)
        {
            return ModDiscoveryDispatch.Denied(ModDiscoveryFailure.ServerNotFound);
        }

        AuthorizationDecision decision = await _permissions
            .EvaluateAsync(user, Permissions.ModView, server: server, cancellationToken).ConfigureAwait(false);
        if (!decision.IsAllowed)
        {
            return ModDiscoveryDispatch.Denied(ModDiscoveryFailure.NotAuthorized);
        }

        // A read: enqueue the non-mutating discovery, not audited. The observed inventory returns on completion
        // into the in-memory inventory cache for the live UI.
        Operation operation = await _operations.EnqueueAsync(
            new EnqueueOperationRequest(
                resolved.AgentId, OperationKind.ModDiscovery, IsMutating: false, Guid.NewGuid().ToString("N"),
                ServerId: server),
            user,
            cancellationToken).ConfigureAwait(false);

        return ModDiscoveryDispatch.Success(operation.Id);
    }
}
