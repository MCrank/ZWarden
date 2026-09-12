using ZWarden.Domain.Audit;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Audit;

/// <summary>
/// A tenant-scoped repository over <see cref="AuditEvent"/> (ADR 0016; trust-boundaries §9 rule 4). Every
/// read builds on the filtered query root, so there is no path that returns another tenant's audit trail.
/// Appends only — the audit store has no update or delete path (ADR 0019). The filtered query surface for
/// the viewer is added by the query service (F6 S4) over <see cref="TenantScopedRepository{TEntity}.Entities"/>.
/// </summary>
public sealed class AuditEventRepository : TenantScopedRepository<AuditEvent>
{
    public AuditEventRepository(ZWardenDbContext context)
        : base(context)
    {
    }
}
