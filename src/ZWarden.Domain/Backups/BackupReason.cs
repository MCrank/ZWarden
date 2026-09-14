namespace ZWarden.Domain.Backups;

/// <summary>
/// Why a <see cref="Backup"/> was taken — the retention metadata a later scheduler (F26) prunes on, and the
/// operator-facing distinction between a backup someone asked for and one a risky Operation took for safety.
/// Stored by name (never its ordinal), so entries may be reordered but not renumbered.
/// </summary>
public enum BackupReason
{
    /// <summary>An operator explicitly asked for this backup (the <c>Backup.Create</c> action). The default kind,
    /// retained until an operator deletes it or a future retention policy (F26) prunes it.</summary>
    Manual = 0,

    /// <summary>ZWarden took this backup automatically before a risky Operation, through the pre-operation backup
    /// API (F24's <c>IPreOperationBackup</c> seam). Recorded so a protective backup is distinguishable from an
    /// operator's own — F25's restore and F26's retention both care which is which. F24 ships the seam; the first
    /// real caller is F25's protective backup.</summary>
    PreOperation = 1,
}
