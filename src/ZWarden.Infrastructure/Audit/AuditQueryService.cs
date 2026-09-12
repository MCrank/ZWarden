using ZWarden.Application.Audit;
using ZWarden.Domain.Audit;

namespace ZWarden.Infrastructure.Audit;

/// <summary>
/// The durable <see cref="IAuditQuery"/> (F6). It reads through the tenant-scoped
/// <see cref="AuditEventRepository"/> — so every result is scoped to the ambient tenant (ADR 0016) — and
/// projects each entity to a non-secret <see cref="AuditEventView"/> for the viewer.
/// </summary>
public sealed class AuditQueryService : IAuditQuery
{
    private readonly AuditEventRepository _repository;

    public AuditQueryService(AuditEventRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public async Task<IReadOnlyList<AuditEventView>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<AuditEvent> events = await _repository.QueryAsync(query, cancellationToken).ConfigureAwait(false);
        return [.. events.Select(ToView)];
    }

    public Task<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default)
        => _repository.CountMatchingAsync(query, cancellationToken);

    private static AuditEventView ToView(AuditEvent audit) => new(
        audit.Id,
        audit.OccurredAt,
        audit.Action,
        audit.Outcome,
        audit.ActorUserId,
        audit.ServerId,
        audit.CorrelationId,
        audit.Detail);
}
