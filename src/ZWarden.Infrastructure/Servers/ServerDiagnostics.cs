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
/// The tenant-scoped Server diagnostics service (F18): run an on-demand RCON health check as a durable,
/// authorized, audited Operation — a near-verbatim sibling of <see cref="ServerLifecycle"/>, differing only in
/// that the Operation is <b>non-mutating</b>. It is <b>fail-closed</b> (ADR 0018): it authorizes the
/// server-scoped <c>Server.Diagnostics</c> permission against the specific Server and resolves the Server
/// through the tenant filter (a foreign or unknown Server is <see cref="ServerLifecycleFailure.ServerNotFound"/>),
/// audits (F6, ADR 0019), and enqueues a non-mutating <see cref="OperationKind.RconHealthProbe"/> on the
/// Server's Agent. Being non-mutating it never claims the per-server lock (ADR 0022), so it never contends with
/// an in-flight lifecycle Operation and never surfaces <see cref="ServerLifecycleFailure.ServerBusy"/>. The
/// Agent, not this service, opens the RCON connection and reports the reachable/authenticated result.
/// </summary>
public sealed class ServerDiagnostics : IServerDiagnostics
{
    private readonly ServerRepository _servers;
    private readonly IPermissionChecker _permissions;
    private readonly IOperationCoordinator _operations;
    private readonly IAuditWriter _audit;

    public ServerDiagnostics(
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
    public async Task<ServerLifecycleResult> CheckRconHealthAsync(
        UserId user, ServerId server, CancellationToken cancellationToken = default)
    {
        // Resolve first, through the tenant filter: an unknown or foreign-tenant Server is ServerNotFound, and
        // gives the server-scoped authorization a concrete resource to check.
        Server? resolved = await _servers.FindByIdAsync(server, cancellationToken).ConfigureAwait(false);
        if (resolved is null)
        {
            return ServerLifecycleResult.Denied(ServerLifecycleFailure.ServerNotFound);
        }

        AuthorizationDecision decision = await _permissions
            .EvaluateAsync(user, Permissions.ServerDiagnostics, server: server, cancellationToken).ConfigureAwait(false);
        if (!decision.IsAllowed)
        {
            return ServerLifecycleResult.Denied(ServerLifecycleFailure.NotAuthorized);
        }

        // A non-mutating, server-scoped Operation (ADR 0022): it names the Server (RCON is per-server) but does
        // not take the per-server lock, so it never contends and never throws ServerBusyException.
        Operation operation = await _operations.EnqueueAsync(
            new EnqueueOperationRequest(
                resolved.AgentId, OperationKind.RconHealthProbe, IsMutating: false, Guid.NewGuid().ToString("N"),
                ServerId: server),
            user,
            cancellationToken).ConfigureAwait(false);

        await _audit.WriteAsync(
            new AuditEntry(ServerAuditActions.RconChecked, AuditOutcome.Succeeded, user, server, $"operation {operation.Id}"),
            cancellationToken).ConfigureAwait(false);

        return ServerLifecycleResult.Success(operation.Id);
    }
}
