using Microsoft.Extensions.Options;
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
/// It also fails an Operation still <see cref="OperationState.Pending"/> past
/// <see cref="OperationEngineOptions.PendingDispatchWindow"/> (#383). Such an Operation was never dispatched,
/// because its Agent was offline, and nothing would ever send it; left alone, a mutating one holds the server's lock
/// forever.
/// </para>
/// <para>
/// Like the F10 connection sweeper, it runs in a system scope for the default tenant (#297), which in single-tenant
/// v1.0 covers every Operation (ADR 0016). A hosted multi-tenant deployment (v1.1) would sweep per tenant.
/// </para>
/// </summary>
public sealed class OperationReaper
{
    /// <summary>The non-secret failure reason recorded for a lease-expired Operation.</summary>
    internal const string LeaseExpiredReason = "lease expired — agent did not report completion";

    /// <summary>The non-secret failure reason recorded for an Operation never dispatched within its window (#383).</summary>
    internal const string NeverDispatchedReason = "the host was offline, so this operation never started";

    private readonly ZWardenDbContext _context;
    private readonly OperationRepository _operations;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _clock;
    private readonly OperationEngineOptions _options;

    public OperationReaper(
        ZWardenDbContext context,
        OperationRepository operations,
        IAuditWriter audit,
        TimeProvider clock,
        IOptions<OperationEngineOptions> options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);
        _context = context;
        _operations = operations;
        _audit = audit;
        _clock = clock;
        _options = options.Value;
    }

    /// <summary>Fails every Operation whose lease has expired, or that stayed Pending past the dispatch window, as of
    /// now, auditing each. Returns how many were reaped.</summary>
    public async Task<int> ReapAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _clock.GetUtcNow();
        IReadOnlyList<Operation> expired = await _operations.FindExpiredLeasesAsync(now, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<Operation> stalePending = await _operations
            .FindStalePendingAsync(now - _options.PendingDispatchWindow, cancellationToken)
            .ConfigureAwait(false);
        if (expired.Count == 0 && stalePending.Count == 0)
        {
            return 0;
        }

        List<(Operation Operation, string Reason)> reaped =
            [.. expired.Select(op => (op, LeaseExpiredReason)), .. stalePending.Select(op => (op, NeverDispatchedReason))];
        foreach ((Operation op, string reason) in reaped)
        {
            op.Fail(reason, now);
        }

        // Persist the failures ourselves — do not rely on the audit writer's save as the flush.
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach ((Operation op, string reason) in reaped)
        {
            await _audit.WriteAsync(
                new AuditEntry(
                    OperationAuditActions.Failed,
                    AuditOutcome.Failed,
                    ServerId: op.ServerId,
                    Detail: $"{op.Kind} {op.Id}; {reason}"),
                cancellationToken).ConfigureAwait(false);
        }

        return reaped.Count;
    }
}
