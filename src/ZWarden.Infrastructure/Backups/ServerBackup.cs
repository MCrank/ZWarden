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
/// The tenant-scoped backup service (F24): take and delete a Server's backups as durable, authorized, audited
/// Operations. Both verbs are <b>fail-closed</b> (ADR 0018) — they authorize their own server-scoped permission
/// against the specific Server and resolve through the tenant filter, so a foreign or unknown target never leaks
/// across tenants. A take is a <b>mutating</b> Operation (per-server lock, ADR 0022 — a second in-flight mutation
/// is <see cref="BackupRequestFailure.ServerBusy"/>); a delete is <b>non-mutating</b> (it only removes an archive
/// file). The Agent, not this service, does the host file work and re-authorizes ownership locally
/// (trust-boundaries §4).
/// </summary>
public sealed class ServerBackup : IServerBackup, IPreOperationBackup
{
    private readonly ServerRepository _servers;
    private readonly BackupRepository _backups;
    private readonly IPermissionChecker _permissions;
    private readonly IOperationCoordinator _operations;
    private readonly IAuditWriter _audit;

    public ServerBackup(
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
    public Task<BackupRequestResult> CreateAsync(
        UserId user, ServerId server, BackupReason reason, CancellationToken cancellationToken = default)
        => CreateInternalAsync(user, server, reason, cancellationToken);

    /// <inheritdoc />
    public Task<BackupRequestResult> EnsureBackupAsync(
        UserId user, ServerId server, CancellationToken cancellationToken = default)
        => CreateInternalAsync(user, server, BackupReason.PreOperation, cancellationToken);

    private async Task<BackupRequestResult> CreateInternalAsync(
        UserId user, ServerId serverId, BackupReason reason, CancellationToken cancellationToken)
    {
        // Resolve first, through the tenant filter: an unknown or foreign-tenant Server is ServerNotFound, and
        // gives the server-scoped authorization a concrete resource to check.
        Server? server = await _servers.FindByIdAsync(serverId, cancellationToken).ConfigureAwait(false);
        if (server is null)
        {
            return BackupRequestResult.Denied(BackupRequestFailure.ServerNotFound);
        }

        AuthorizationDecision decision = await _permissions
            .EvaluateAsync(user, Permissions.BackupCreate, server: serverId, cancellationToken).ConfigureAwait(false);
        if (!decision.IsAllowed)
        {
            return BackupRequestResult.Denied(BackupRequestFailure.NotAuthorized);
        }

        try
        {
            // A mutating, server-scoped Operation on the Server's Agent (ADR 0022); the reason rides the payload so
            // the completion ingest records it as retention metadata. Fresh intent ⇒ fresh idempotency key.
            Operation operation = await _operations.EnqueueAsync(
                new EnqueueOperationRequest(
                    server.AgentId, OperationKind.Backup, IsMutating: true, Guid.NewGuid().ToString("N"),
                    ServerId: serverId, CommandPayload: new BackupCommandPayload(Reason: reason.ToString()).ToJson()),
                user,
                cancellationToken).ConfigureAwait(false);

            await _audit.WriteAsync(
                new AuditEntry(ServerAuditActions.BackedUp, AuditOutcome.Succeeded, user, serverId, $"operation {operation.Id}"),
                cancellationToken).ConfigureAwait(false);

            return BackupRequestResult.Success(operation.Id);
        }
        catch (ServerBusyException)
        {
            await _audit.WriteAsync(
                new AuditEntry(ServerAuditActions.BackedUp, AuditOutcome.Failed, user, serverId, "server busy"),
                cancellationToken).ConfigureAwait(false);
            return BackupRequestResult.Denied(BackupRequestFailure.ServerBusy);
        }
    }

    /// <inheritdoc />
    public async Task<BackupDeletionOutcome> DeleteAsync(
        UserId user, BackupId backupId, CancellationToken cancellationToken = default)
    {
        // Resolve the backup through the tenant filter first: an unknown or foreign-tenant backup is BackupNotFound,
        // and its Server is the resource the server-scoped authorization checks.
        Backup? backup = await _backups.FindByIdAsync(backupId, cancellationToken).ConfigureAwait(false);
        if (backup is null)
        {
            return BackupDeletionOutcome.Denied(BackupDeletionFailure.BackupNotFound);
        }

        AuthorizationDecision decision = await _permissions
            .EvaluateAsync(user, Permissions.BackupDelete, server: backup.ServerId, cancellationToken).ConfigureAwait(false);
        if (!decision.IsAllowed)
        {
            return BackupDeletionOutcome.Denied(BackupDeletionFailure.NotAuthorized);
        }

        // A non-mutating, server-scoped Operation on the backup's Agent: it removes only the archive file, so it
        // takes no per-server lock and never conflicts with a lifecycle Operation. The record id + archive name ride
        // the payload — the dispatcher sends the name, the ingest removes the record by id on confirmed completion.
        Operation operation = await _operations.EnqueueAsync(
            new EnqueueOperationRequest(
                backup.AgentId, OperationKind.DeleteBackup, IsMutating: false, Guid.NewGuid().ToString("N"),
                ServerId: backup.ServerId,
                CommandPayload: new BackupCommandPayload(BackupId: backup.Id.ToString(), ArchiveName: backup.ArchiveName).ToJson()),
            user,
            cancellationToken).ConfigureAwait(false);

        await _audit.WriteAsync(
            new AuditEntry(ServerAuditActions.BackupDeleted, AuditOutcome.Succeeded, user, backup.ServerId, $"operation {operation.Id}"),
            cancellationToken).ConfigureAwait(false);

        return BackupDeletionOutcome.Success(operation.Id);
    }
}
