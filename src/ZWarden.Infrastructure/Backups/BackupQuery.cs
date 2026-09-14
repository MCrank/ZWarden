using ZWarden.Application.Backups;
using ZWarden.Domain.Backups;
using ZWarden.Domain.Ids;

namespace ZWarden.Infrastructure.Backups;

/// <summary>
/// The tenant-scoped read surface over backups for the operator UI (F24, ADR 0016). It builds on the
/// <see cref="BackupRepository"/>'s tenant-filtered query root, so a caller only ever sees the current tenant's
/// backups. It maps the aggregate onto a display DTO — never the entity — so the UI never touches domain internals.
/// </summary>
public sealed class BackupQuery : IBackupQuery
{
    private readonly BackupRepository _backups;

    public BackupQuery(BackupRepository backups)
    {
        ArgumentNullException.ThrowIfNull(backups);
        _backups = backups;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BackupSummary>> ListForServerAsync(
        ServerId server, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Backup> backups = await _backups.ListForServerAsync(server, cancellationToken).ConfigureAwait(false);
        return [.. backups.Select(b => new BackupSummary(
            b.Id, b.ArchiveName, b.SizeBytes, b.Sha256, b.Reason, b.CreatedAt, b.ExpiresAt))];
    }
}
