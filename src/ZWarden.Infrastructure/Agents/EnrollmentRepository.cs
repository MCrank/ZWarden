using Microsoft.EntityFrameworkCore;
using ZWarden.Domain.Enrollments;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Agents;

/// <summary>
/// A tenant-scoped repository over <see cref="Enrollment"/> (ADR 0016; trust-boundaries §9 rule 4). Every
/// read builds on the filtered query root, so no path returns another tenant's enrollments — the exchange's
/// hash lookup is, by construction, tenant-scoped (it runs under the ambient/default tenant, F10A adds the
/// hosted, tenant-bound path in v1.1).
/// </summary>
public sealed class EnrollmentRepository : TenantScopedRepository<Enrollment>
{
    public EnrollmentRepository(ZWardenDbContext context)
        : base(context)
    {
    }

    /// <summary>The ambient tenant's enrollment with the given secret hash, or <c>null</c>.</summary>
    public async Task<Enrollment?> FindBySecretHashAsync(
        string secretHash,
        CancellationToken cancellationToken = default)
        => await Entities.FirstOrDefaultAsync(e => e.SecretHash == secretHash, cancellationToken)
            .ConfigureAwait(false);

    /// <summary>The ambient tenant's enrollment with the given id, or <c>null</c>.</summary>
    public async Task<Enrollment?> FindByIdAsync(
        EnrollmentId id,
        CancellationToken cancellationToken = default)
        => await Entities.FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
            .ConfigureAwait(false);
}
