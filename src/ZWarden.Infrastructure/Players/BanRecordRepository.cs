using Microsoft.EntityFrameworkCore;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Players;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Players;

/// <summary>
/// A tenant-scoped repository over <see cref="BanRecord"/> (ADR 0016, F19). Every read builds on the filtered
/// query root, so no path returns another tenant's ban registry. Usernames match exactly (PZ argument values are
/// case-sensitive — research §7), which is also what the active-ban uniqueness index keys on.
/// </summary>
public sealed class BanRecordRepository : TenantScopedRepository<BanRecord>
{
    public BanRecordRepository(ZWardenDbContext context)
        : base(context)
    {
    }

    /// <summary>The ambient tenant's <see cref="BanStatus.Active"/> ban for the given Server and username, or
    /// <c>null</c> — used to lift a ban and to avoid recording a duplicate active ban.</summary>
    public async Task<BanRecord?> FindActiveAsync(ServerId server, string username, CancellationToken cancellationToken = default)
        => await Entities
            .FirstOrDefaultAsync(
                b => b.ServerId == server && b.Username == username && b.Status == BanStatus.Active,
                cancellationToken)
            .ConfigureAwait(false);

    /// <summary>The ambient tenant's ban records for a Server, newest issued first — the ban-list read (F19).</summary>
    public async Task<IReadOnlyList<BanRecord>> ListForServerAsync(ServerId server, CancellationToken cancellationToken = default)
        => await Entities
            .Where(b => b.ServerId == server)
            .OrderByDescending(b => b.IssuedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}
