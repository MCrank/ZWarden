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
    /// The ambient tenant's non-terminal mutating Operation on <paramref name="serverId"/> — at most one, the row
    /// holding the per-server lock (ADR 0022) — or <c>null</c>. Filtered wholly in SQL (the live header polls
    /// this, so it must not load the Server's history): equality against the string-converted state translates;
    /// only the <c>is A or B</c> pattern and the computed <c>IsTerminal</c> do not.
    /// </summary>
    public async Task<Operation?> FindActiveMutatingForServerAsync(ServerId serverId, CancellationToken cancellationToken = default)
        => await Entities
            .Where(o => o.ServerId == serverId && o.IsMutating
                && (o.State == OperationState.Pending
                    || o.State == OperationState.Running
                    || o.State == OperationState.Cancelling))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// The ambient tenant's most recently finished (succeeded, failed or cancelled) mutating Operation on
    /// <paramref name="serverId"/>, or <c>null</c> (#266). Mutating Operations on one Server never overlap (the
    /// per-server lock, ADR 0022), so the most recently <i>enqueued</i> finished one is also the latest completion —
    /// and the id is a UUIDv7, time-ordered in both providers, where SQLite cannot ORDER BY a <c>DateTimeOffset</c>.
    /// </summary>
    public async Task<Operation?> FindLatestFinishedMutatingForServerAsync(ServerId serverId, CancellationToken cancellationToken = default)
        => await Entities
            .Where(o => o.ServerId == serverId && o.IsMutating
                && (o.State == OperationState.Succeeded
                    || o.State == OperationState.Failed
                    || o.State == OperationState.Cancelled))
            .OrderByDescending(o => o.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Every non-terminal mutating Operation in the ambient tenant — at most one per Server, the rows holding the
    /// per-server locks (ADR 0022). The fleet board polls this (#253) instead of one query per Server; the set is
    /// bounded by the tenant's Server count. Same SQL-translatable state filter as the single-Server read.
    /// </summary>
    public async Task<IReadOnlyList<Operation>> ListActiveMutatingAsync(CancellationToken cancellationToken = default)
        => await Entities
            .Where(o => o.IsMutating
                && (o.State == OperationState.Pending
                    || o.State == OperationState.Running
                    || o.State == OperationState.Cancelling))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

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
