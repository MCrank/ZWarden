using ZWarden.Domain.Ids;

namespace ZWarden.Application.Backups;

/// <summary>
/// The operator-facing restore service (F25): restore a Server's world data from one of its backups as a durable,
/// authorized, audited Operation. <see cref="RestoreAsync"/> authorizes the server-scoped <c>Backup.Restore</c>
/// against the backup's Server (ADR 0018, fail-closed), resolves the backup through the tenant filter (a foreign or
/// unknown backup is <see cref="RestoreRequestFailure.BackupNotFound"/>), refuses early when the Server was last
/// observed running (<see cref="RestoreRequestFailure.ServerRunning"/> — the operator must stop it first), audits,
/// and enqueues a <b>mutating, server-scoped</b> restore Operation on the backup's Agent (per-server lock, ADR 0022
/// — a conflicting in-flight Operation is <see cref="RestoreRequestFailure.ServerBusy"/>). It never touches the
/// Agent host directly: the Agent re-verifies the archive checksum, takes an inline protective backup, and swaps the
/// world atomically, re-authorizing ownership locally (trust-boundaries §4, ADR 0029).
/// </summary>
public interface IServerRestore
{
    /// <summary>Restores the Server's world from the backup identified by <paramref name="backupId"/>, authorized by
    /// the server-scoped <c>Backup.Restore</c>. Returns the enqueued Operation whose state the caller polls, or a
    /// typed refusal.</summary>
    Task<RestoreRequestResult> RestoreAsync(UserId user, BackupId backupId, CancellationToken cancellationToken = default);
}
