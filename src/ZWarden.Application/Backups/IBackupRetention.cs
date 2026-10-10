using ZWarden.Domain.Ids;

namespace ZWarden.Application.Backups;

/// <summary>
/// Retention for automatic backups (#379): a server keeps its newest <c>ZWarden:Backups:KeepAutomatic</c>
/// <see cref="Domain.Backups.BackupReason.PreOperation"/> backups, and each older one is deleted through the existing
/// <c>DeleteBackup</c> Operation and audited. A <see cref="Domain.Backups.BackupReason.Manual"/> backup is never pruned.
/// Called by the completion ingest after it records an automatic backup. It is policy, not an operator action, so it
/// checks no permission and acts as the system; reads go through the tenant filter like every backup read.
/// </summary>
public interface IBackupRetention
{
    /// <summary>Deletes the server's automatic backups beyond the configured number, oldest first. Returns how many
    /// deletions were started. A backup whose deletion is already in flight is not deleted again; a deletion that
    /// can't start (the host went offline) is left for the next prune.</summary>
    Task<int> PruneAsync(ServerId server, CancellationToken cancellationToken = default);
}
