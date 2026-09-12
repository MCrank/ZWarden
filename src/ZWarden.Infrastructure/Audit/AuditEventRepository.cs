using Microsoft.EntityFrameworkCore;
using ZWarden.Application.Audit;
using ZWarden.Domain.Audit;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Audit;

/// <summary>
/// A tenant-scoped repository over <see cref="AuditEvent"/> (ADR 0016; trust-boundaries §9 rule 4). Every
/// read builds on the filtered query root, so there is no path that returns another tenant's audit trail.
/// Appends only — the audit store has no update or delete path (ADR 0019).
/// </summary>
public sealed class AuditEventRepository : TenantScopedRepository<AuditEvent>
{
    public AuditEventRepository(ZWardenDbContext context)
        : base(context)
    {
    }

    /// <summary>The ambient tenant's audit events matching <paramref name="query"/>, newest-first, paged.</summary>
    public async Task<IReadOnlyList<AuditEvent>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IQueryable<AuditEvent> matching = Filter(query)
            .OrderByDescending(a => a.OccurredAt)
            .ThenByDescending(a => a.Id)
            .Skip(query.Skip);

        if (query.Take > 0)
        {
            matching = matching.Take(query.Take);
        }

        return await matching.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The total number of the ambient tenant's audit events matching <paramref name="query"/>
    /// (ignoring paging).</summary>
    public Task<int> CountMatchingAsync(AuditQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Filter(query).CountAsync(cancellationToken);
    }

    private IQueryable<AuditEvent> Filter(AuditQuery query)
    {
        IQueryable<AuditEvent> events = Entities;

        if (query.From is { } from)
        {
            events = events.Where(a => a.OccurredAt >= from);
        }

        if (query.To is { } to)
        {
            events = events.Where(a => a.OccurredAt <= to);
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            events = events.Where(a => a.Action == query.Action);
        }

        if (query.ActorUserId is { } actor)
        {
            events = events.Where(a => a.ActorUserId == actor);
        }

        if (query.ServerId is { } server)
        {
            events = events.Where(a => a.ServerId == server);
        }

        if (query.Outcome is { } outcome)
        {
            events = events.Where(a => a.Outcome == outcome);
        }

        if (!string.IsNullOrWhiteSpace(query.CorrelationId))
        {
            events = events.Where(a => a.CorrelationId == query.CorrelationId);
        }

        return events;
    }
}
