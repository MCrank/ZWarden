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
        // The DateTimeOffset comparison and the string-converted enum are not translatable inside the
        // predicate, so the filter runs in memory over the tenant's Operations — as AgentRepository does for
        // the connection sweep. Acceptable at v1.0's operation volumes; a later feature can index/narrow this
        // if history growth warrants it.
        IReadOnlyList<Operation> all = await ListAsync(cancellationToken).ConfigureAwait(false);

        return all
            .Where(o => o.State is OperationState.Running or OperationState.Cancelling
                && o.LeaseExpiresAt is { } lease
                && lease < now)
            .ToList();
    }
}
