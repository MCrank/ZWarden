using ZWarden.Domain.Backups;
using ZWarden.Domain.Ids;

namespace ZWarden.Application.Backups;

/// <summary>A backup as the operator surface reads it (F24): the integrity and retention metadata, never the archive
/// bytes. Tenant-scoped by the query that produces it.</summary>
/// <param name="Id">The backup id (<c>bkp-</c>).</param>
/// <param name="ArchiveName">The archive's relative locator (its file name on the Agent host).</param>
/// <param name="SizeBytes">The produced archive's size in bytes.</param>
/// <param name="Sha256">The lowercase-hex SHA-256 over the archive.</param>
/// <param name="Reason">Why the backup was taken (<see cref="BackupReason.Manual"/> / <see cref="BackupReason.PreOperation"/>).</param>
/// <param name="CreatedAt">When the archive was written (UTC).</param>
/// <param name="ExpiresAt">An optional retention expiry hint, or <c>null</c>.</param>
public sealed record BackupSummary(
    BackupId Id,
    string ArchiveName,
    long SizeBytes,
    string Sha256,
    BackupReason Reason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt);

/// <summary>The tenant-scoped read surface over backups for the operator UI (F24). Reads go through the tenant
/// filter (ADR 0016), so a caller only ever sees the current tenant's backups.</summary>
public interface IBackupQuery
{
    /// <summary>The current tenant's backups for a Server, newest first.</summary>
    Task<IReadOnlyList<BackupSummary>> ListForServerAsync(ServerId server, CancellationToken cancellationToken = default);
}
