using ZWarden.Application.Audit;
using ZWarden.Domain.Audit;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Infrastructure.Audit;

/// <summary>
/// The durable <see cref="IAuditWriter"/> (F6; ADR 0019). It builds an <see cref="AuditEvent"/> from the
/// entry, stamping <c>OccurredAt</c> from the <see cref="TimeProvider"/> and the correlation id from
/// <see cref="ICorrelationContext"/>; the ambient tenant is stamped by the ownership interceptor on insert
/// (ADR 0016). It only ever adds — the store has no update or delete path.
/// </summary>
public sealed class AuditWriter : IAuditWriter
{
    private readonly ZWardenDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly ICorrelationContext _correlation;

    public AuditWriter(ZWardenDbContext context, TimeProvider timeProvider, ICorrelationContext correlation)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(correlation);
        _context = context;
        _timeProvider = timeProvider;
        _correlation = correlation;
    }

    public async Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        AuditEvent audit = AuditEvent.Create(
            entry.Action,
            entry.Outcome,
            _timeProvider.GetUtcNow(),
            entry.ActorUserId,
            entry.ServerId,
            _correlation.CurrentCorrelationId,
            entry.Detail);

        _context.Add(audit);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
