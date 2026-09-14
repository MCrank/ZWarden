using ZWarden.Application.Audit;
using ZWarden.Application.Authorization;
using ZWarden.Application.Backups;
using ZWarden.Application.Operations;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Backups;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Servers;

namespace ZWarden.Infrastructure.Backups;

/// <summary>
/// The tenant-scoped restore service (F25): restore a Server's world from a backup as a durable, authorized, audited
/// Operation. Like every mutating verb it is <b>fail-closed</b> (ADR 0018) — it authorizes the server-scoped
/// <c>Backup.Restore</c> against the backup's specific Server and resolves the backup through the tenant filter, so a
/// foreign or unknown target never leaks across tenants. It refuses early when the Server was last observed running
/// (an advisory pre-check; the Agent makes the authoritative refusal), then enqueues a <b>mutating</b> Operation
/// (per-server lock, ADR 0022 — a second in-flight mutation is <see cref="RestoreRequestFailure.ServerBusy"/>). The
/// Agent, not this service, verifies the archive, takes the inline protective backup, and swaps the world atomically
/// (ADR 0029), re-authorizing ownership locally (trust-boundaries §4).
/// </summary>
public sealed class ServerRestore : IServerRestore
{
    private readonly ServerRepository _servers;
    private readonly BackupRepository _backups;
    private readonly IPermissionChecker _permissions;
    private readonly IOperationCoordinator _operations;
    private readonly IAuditWriter _audit;

    public ServerRestore(
        ServerRepository servers,
        BackupRepository backups,
        IPermissionChecker permissions,
        IOperationCoordinator operations,
        IAuditWriter audit)
    {
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(backups);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(audit);
        _servers = servers;
        _backups = backups;
        _permissions = permissions;
        _operations = operations;
        _audit = audit;
    }

    /// <inheritdoc />
    public async Task<RestoreRequestResult> RestoreAsync(
        UserId user, BackupId backupId, CancellationToken cancellationToken = default)
    {
        // Resolve the backup through the tenant filter first: an unknown or foreign-tenant backup is BackupNotFound,
        // and its Server is the resource the server-scoped authorization checks.
        Backup? backup = await _backups.FindByIdAsync(backupId, cancellationToken).ConfigureAwait(false);
        if (backup is null)
        {
            return RestoreRequestResult.Denied(RestoreRequestFailure.BackupNotFound);
        }

        AuthorizationDecision decision = await _permissions
            .EvaluateAsync(user, Permissions.BackupRestore, server: backup.ServerId, cancellationToken).ConfigureAwait(false);
        if (!decision.IsAllowed)
        {
            return RestoreRequestResult.Denied(RestoreRequestFailure.NotAuthorized);
        }

        // Advisory stopped pre-check: a restore overwrites the world, so refuse early when the Server was last
        // observed running rather than enqueue an Operation the Agent will only refuse (trust-boundaries §3). The
        // Server resolves through the tenant filter; a backup with no visible Server is treated as not found.
        Server? server = await _servers.FindByIdAsync(backup.ServerId, cancellationToken).ConfigureAwait(false);
        if (server is null)
        {
            return RestoreRequestResult.Denied(RestoreRequestFailure.BackupNotFound);
        }

        if (server.LastRunState == ServerRunState.Running)
        {
            await _audit.WriteAsync(
                new AuditEntry(ServerAuditActions.Restored, AuditOutcome.Failed, user, backup.ServerId, "server running"),
                cancellationToken).ConfigureAwait(false);
            return RestoreRequestResult.Denied(RestoreRequestFailure.ServerRunning);
        }

        try
        {
            // A mutating, server-scoped Operation on the backup's Agent (ADR 0022). The target backup id, archive
            // name, and checksum ride the payload: the dispatcher sends the name + checksum to the Agent, which
            // re-verifies before unpacking (ADR 0028/0029). Fresh intent ⇒ fresh idempotency key.
            Operation operation = await _operations.EnqueueAsync(
                new EnqueueOperationRequest(
                    backup.AgentId, OperationKind.Restore, IsMutating: true, Guid.NewGuid().ToString("N"),
                    ServerId: backup.ServerId,
                    CommandPayload: new RestoreCommandPayload(
                        BackupId: backup.Id.ToString(), ArchiveName: backup.ArchiveName, Sha256: backup.Sha256).ToJson()),
                user,
                cancellationToken).ConfigureAwait(false);

            await _audit.WriteAsync(
                new AuditEntry(ServerAuditActions.Restored, AuditOutcome.Succeeded, user, backup.ServerId, $"operation {operation.Id}"),
                cancellationToken).ConfigureAwait(false);

            return RestoreRequestResult.Success(operation.Id);
        }
        catch (ServerBusyException)
        {
            await _audit.WriteAsync(
                new AuditEntry(ServerAuditActions.Restored, AuditOutcome.Failed, user, backup.ServerId, "server busy"),
                cancellationToken).ConfigureAwait(false);
            return RestoreRequestResult.Denied(RestoreRequestFailure.ServerBusy);
        }
    }
}
