using ZWarden.Domain.Backups;
using ZWarden.Domain.Ids;

namespace ZWarden.Application.Backups;

/// <summary>
/// The operator-facing backup service (F24): take and delete a Server's backups as durable, authorized, audited
/// Operations. <see cref="CreateAsync"/> authorizes the server-scoped <c>Backup.Create</c> against the specific
/// Server (ADR 0018, fail-closed), resolves the Server through the tenant filter (a foreign Server is
/// <see cref="BackupRequestFailure.ServerNotFound"/>), audits, and enqueues a <b>mutating, server-scoped</b> backup
/// Operation on the Server's Agent (per-server lock, ADR 0022 — a conflicting in-flight Operation is
/// <see cref="BackupRequestFailure.ServerBusy"/>). <see cref="DeleteAsync"/> authorizes <c>Backup.Delete</c>,
/// resolves the backup through the tenant filter, and enqueues a <b>non-mutating</b> deletion Operation; the record
/// is removed on that Operation's confirmed completion. Neither method touches the Agent host directly — the Agent
/// runs the file work and re-authorizes ownership locally (trust-boundaries §4).
/// </summary>
public interface IServerBackup
{
    /// <summary>Takes a backup of the Server's world data, authorized by the server-scoped <c>Backup.Create</c>.
    /// The <paramref name="reason"/> is recorded as retention metadata (F24); the operator surface passes
    /// <see cref="BackupReason.Manual"/>, the pre-operation seam passes <see cref="BackupReason.PreOperation"/>.</summary>
    Task<BackupRequestResult> CreateAsync(
        UserId user, ServerId server, BackupReason reason, CancellationToken cancellationToken = default);

    /// <summary>Deletes a backup (record and Agent-side archive), authorized by the server-scoped
    /// <c>Backup.Delete</c>. The archive removal is a non-mutating Operation on the backup's Agent; the record is
    /// removed on its confirmed completion.</summary>
    Task<BackupDeletionOutcome> DeleteAsync(UserId user, BackupId backupId, CancellationToken cancellationToken = default);
}
