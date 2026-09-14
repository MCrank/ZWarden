using Microsoft.EntityFrameworkCore;
using ZWarden.Domain.Backups;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Backups;

/// <summary>
/// A tenant-scoped repository over <see cref="Backup"/> (ADR 0016, F24). Every read builds on the filtered query
/// root, so no path returns another tenant's backups. Records are write-once, so there is no update path — a
/// backup is added when its Operation completes and removed only by a manual delete.
/// </summary>
public sealed class BackupRepository : TenantScopedRepository<Backup>
{
    public BackupRepository(ZWardenDbContext context)
        : base(context)
    {
    }

    /// <summary>The ambient tenant's backups for a Server, newest first — the per-Server backup list read (F24).</summary>
    public async Task<IReadOnlyList<Backup>> ListForServerAsync(ServerId server, CancellationToken cancellationToken = default)
        => await Entities
            .Where(b => b.ServerId == server)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>The ambient tenant's backup by id, or <c>null</c> — used to resolve a delete target.</summary>
    public async Task<Backup?> FindByIdAsync(BackupId id, CancellationToken cancellationToken = default)
        => await Entities
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken)
            .ConfigureAwait(false);

    /// <summary>Removes a backup record (F24 manual delete). The row is already tenant-scoped by construction, so
    /// only a record the ambient tenant can read is ever removed; deleting the Agent-side archive is a separate
    /// step the caller drives.</summary>
    public void Remove(Backup backup)
    {
        ArgumentNullException.ThrowIfNull(backup);
        Context.Remove(backup);
    }
}
