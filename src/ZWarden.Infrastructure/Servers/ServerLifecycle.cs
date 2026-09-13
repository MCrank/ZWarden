using ZWarden.Application.Audit;
using ZWarden.Application.Authorization;
using ZWarden.Application.Operations;
using ZWarden.Application.Servers;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Servers;

namespace ZWarden.Infrastructure.Servers;

/// <summary>
/// The tenant-scoped Server lifecycle service (F15): start / stop / restart as durable, authorized, audited
/// Operations. Each verb is <b>fail-closed</b> (ADR 0018) — it authorizes its own server-scoped permission
/// against the specific Server, and resolves the Server through the tenant filter, so a foreign or unknown
/// Server is <see cref="ServerLifecycleFailure.ServerNotFound"/> and never leaks across tenants. On success it
/// audits (F6, ADR 0019) and enqueues a <b>mutating, server-scoped</b> Operation on the Server's Agent, which
/// the per-server lock (ADR 0022) serializes — a second lifecycle action while one is in flight surfaces as
/// <see cref="ServerLifecycleFailure.ServerBusy"/>. The Agent, not this service, runs the Docker verb and
/// re-authorizes container ownership locally (F13, trust-boundaries §4).
/// </summary>
public sealed class ServerLifecycle : IServerLifecycle
{
    private readonly ServerRepository _servers;
    private readonly IPermissionChecker _permissions;
    private readonly IOperationCoordinator _operations;
    private readonly IAuditWriter _audit;

    public ServerLifecycle(
        ServerRepository servers,
        IPermissionChecker permissions,
        IOperationCoordinator operations,
        IAuditWriter audit)
    {
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(audit);
        _servers = servers;
        _permissions = permissions;
        _operations = operations;
        _audit = audit;
    }

    /// <inheritdoc />
    public Task<ServerLifecycleResult> StartAsync(UserId user, ServerId server, CancellationToken cancellationToken = default)
        => RunAsync(user, server, Permissions.ServerStart, OperationKind.StartServer, ServerAuditActions.Started, cancellationToken);

    /// <inheritdoc />
    public Task<ServerLifecycleResult> StopAsync(UserId user, ServerId server, CancellationToken cancellationToken = default)
        => RunAsync(user, server, Permissions.ServerStop, OperationKind.StopServer, ServerAuditActions.Stopped, cancellationToken);

    /// <inheritdoc />
    public Task<ServerLifecycleResult> RestartAsync(UserId user, ServerId server, CancellationToken cancellationToken = default)
        => RunAsync(user, server, Permissions.ServerRestart, OperationKind.RestartServer, ServerAuditActions.Restarted, cancellationToken);

    /// <inheritdoc />
    public Task<ServerLifecycleResult> UpdateAsync(UserId user, ServerId server, CancellationToken cancellationToken = default)
        => RunAsync(user, server, Permissions.ServerUpdate, OperationKind.UpdateServer, ServerAuditActions.Updated, cancellationToken);

    private async Task<ServerLifecycleResult> RunAsync(
        UserId user,
        ServerId serverId,
        PermissionDefinition permission,
        OperationKind kind,
        string auditAction,
        CancellationToken cancellationToken)
    {
        // Resolve first, through the tenant filter: an unknown or foreign-tenant Server is ServerNotFound, and
        // gives the server-scoped authorization a concrete resource to check.
        Server? server = await _servers.FindByIdAsync(serverId, cancellationToken).ConfigureAwait(false);
        if (server is null)
        {
            return ServerLifecycleResult.Denied(ServerLifecycleFailure.ServerNotFound);
        }

        AuthorizationDecision decision = await _permissions
            .EvaluateAsync(user, permission, server: serverId, cancellationToken).ConfigureAwait(false);
        if (!decision.IsAllowed)
        {
            return ServerLifecycleResult.Denied(ServerLifecycleFailure.NotAuthorized);
        }

        try
        {
            // A mutating, server-scoped Operation on the Server's Agent (ADR 0022). Each request is a fresh
            // intent, so the idempotency key is fresh; the per-server lock refuses a second in-flight mutation.
            Operation operation = await _operations.EnqueueAsync(
                new EnqueueOperationRequest(
                    server.AgentId, kind, IsMutating: true, Guid.NewGuid().ToString("N"), ServerId: serverId),
                user,
                cancellationToken).ConfigureAwait(false);

            await _audit.WriteAsync(
                new AuditEntry(auditAction, AuditOutcome.Succeeded, user, serverId, $"operation {operation.Id}"),
                cancellationToken).ConfigureAwait(false);

            return ServerLifecycleResult.Success(operation.Id);
        }
        catch (ServerBusyException)
        {
            // The per-server lock (ADR 0022) refused: another mutating Operation is already in flight.
            await _audit.WriteAsync(
                new AuditEntry(auditAction, AuditOutcome.Failed, user, serverId, "server busy"),
                cancellationToken).ConfigureAwait(false);
            return ServerLifecycleResult.Denied(ServerLifecycleFailure.ServerBusy);
        }
    }
}
