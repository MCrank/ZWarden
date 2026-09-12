using Microsoft.EntityFrameworkCore;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Authorization;

/// <summary>
/// A tenant-scoped repository over <see cref="RoleAssignment"/> (ADR 0016; trust-boundaries §9 rule 4).
/// Every read builds on the filtered query root, so there is no path that returns another tenant's
/// assignments — a membership check is, by construction, tenant-scoped.
/// </summary>
public sealed class RoleAssignmentRepository : TenantScopedRepository<RoleAssignment>
{
    public RoleAssignmentRepository(ZWardenDbContext context)
        : base(context)
    {
    }

    /// <summary>The ambient tenant's role assignments for a single user (a membership read).</summary>
    public async Task<IReadOnlyList<RoleAssignment>> ListForUserAsync(
        UserId userId,
        CancellationToken cancellationToken = default)
        => await Entities.Where(a => a.UserId == userId).ToListAsync(cancellationToken).ConfigureAwait(false);
}
