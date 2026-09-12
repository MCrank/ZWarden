using ZWarden.Application.Audit;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Operations;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Infrastructure.Operations;

/// <summary>
/// Fails Operations whose lease has expired (ADR 0022) — the safety net that releases a per-server lock when
/// an Agent dies, disconnects, or hangs mid-work. A <see cref="OperationState.Running"/> or
/// <see cref="OperationState.Cancelling"/> Operation past its <see cref="Operation.LeaseExpiresAt"/> is driven
/// to <see cref="OperationState.Failed"/> (which clears the lease and frees the lock) and audited. The
/// optimistic <c>Version</c> token means a completion that raced the reaper wins: the stale fail conflicts and
/// is retried on the next sweep, by when the Operation is terminal and no longer due.
/// <para>
/// Like the F10 connection sweeper, it runs under the default-tenant fallback, which in single-tenant v1.0
/// covers every Operation (ADR 0016). A hosted multi-tenant deployment (v1.1) would sweep per tenant.
/// </para>
/// </summary>
public sealed class OperationReaper
{
    /// <summary>The non-secret failure reason recorded for a lease-expired Operation.</summary>
    internal const string LeaseExpiredReason = "lease expired — agent did not report completion";

    private readonly ZWardenDbContext _context;
    private readonly OperationRepository _operations;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _clock;

    public OperationReaper(
        ZWardenDbContext context,
        OperationRepository operations,
        IAuditWriter audit,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(clock);
        _context = context;
        _operations = operations;
        _audit = audit;
        _clock = clock;
    }

    /// <summary>Fails every Operation whose lease has expired as of now, auditing each. Returns how many were
    /// reaped.</summary>
    public async Task<int> ReapAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _clock.GetUtcNow();
        IReadOnlyList<Operation> expired = await _operations.FindExpiredLeasesAsync(now, cancellationToken)
            .ConfigureAwait(false);
        if (expired.Count == 0)
        {
            return 0;
        }

        foreach (Operation op in expired)
        {
            op.Fail(LeaseExpiredReason, now);
        }

        // Persist the failures ourselves — do not rely on the audit writer's save as the flush.
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (Operation op in expired)
        {
            await _audit.WriteAsync(
                new AuditEntry(
                    OperationAuditActions.Failed,
                    AuditOutcome.Failed,
                    ServerId: op.ServerId,
                    Detail: $"{op.Kind} {op.Id}; {LeaseExpiredReason}"),
                cancellationToken).ConfigureAwait(false);
        }

        return expired.Count;
    }
}
