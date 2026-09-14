using ZWarden.Domain.Ids;

namespace ZWarden.Application.Backups;

/// <summary>
/// The pre-operation backup API (F24): a callable seam a risky Operation can invoke to take a protective backup
/// <b>before</b> proceeding, tagged <see cref="Domain.Backups.BackupReason.PreOperation"/> so it is distinguishable
/// from an operator's own backup. F24 ships this seam and wires <b>no</b> Operation to it — each risky feature calls
/// it on its own schedule, and F25's protective backup is the first real caller. It authorizes and enqueues exactly
/// as <see cref="IServerBackup.CreateAsync"/> does; the caller decides whether to proceed on the result (and, later,
/// whether to wait for the backup Operation to finish first).
/// </summary>
public interface IPreOperationBackup
{
    /// <summary>Takes a protective, <c>PreOperation</c>-tagged backup of the Server, authorized by the server-scoped
    /// <c>Backup.Create</c>. Returns the enqueued Operation (or a typed refusal) so the caller can decide how to
    /// proceed.</summary>
    Task<BackupRequestResult> EnsureBackupAsync(UserId user, ServerId server, CancellationToken cancellationToken = default);
}
