using Microsoft.EntityFrameworkCore;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Operations;

/// <summary>
/// A tenant-scoped repository over <see cref="Operation"/> (ADR 0016). Every read builds on the filtered
/// query root, so no path returns another tenant's Operations.
/// </summary>
public sealed class OperationRepository : TenantScopedRepository<Operation>
{
    public OperationRepository(ZWardenDbContext context)
        : base(context)
    {
    }

    /// <summary>The ambient tenant's Operation with the given id, or <c>null</c>.</summary>
    public async Task<Operation?> FindByIdAsync(OperationId id, CancellationToken cancellationToken = default)
        => await Entities.FirstOrDefaultAsync(o => o.Id == id, cancellationToken).ConfigureAwait(false);

    /// <summary>The ambient tenant's Operation with the given idempotency key, or <c>null</c> (PRD 20).</summary>
    public async Task<Operation?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default)
        => await Entities.FirstOrDefaultAsync(o => o.IdempotencyKey == idempotencyKey, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// The ambient tenant's active Operations whose lease has expired as of <paramref name="now"/> — the ones
    /// the reaper fails to release their per-server lock (ADR 0022). Only <see cref="OperationState.Running"/>
    /// and <see cref="OperationState.Cancelling"/> carry a lease (terminal states clear it), so a non-null
    /// expired lease already implies an active Operation; the state is re-checked in memory because the
    /// string-converted enum is not translatable inside the predicate.
    /// </summary>
    public async Task<IReadOnlyList<Operation>> FindExpiredLeasesAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Operation> due = await Entities
            .Where(o => o.LeaseExpiresAt != null && o.LeaseExpiresAt < now)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return due
            .Where(o => o.State is OperationState.Running or OperationState.Cancelling)
            .ToList();
    }
}
