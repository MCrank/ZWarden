namespace ZWarden.Application.Audit;

/// <summary>
/// The tenant-scoped read surface over the audit trail (F6; ADR 0019). Every read is scoped to the ambient
/// tenant by construction (ADR 0016) — there is no cross-tenant path. The interface lives in Application and
/// references only Domain types.
/// </summary>
public interface IAuditQuery
{
    /// <summary>The matching audit events, newest-first, paged by the query's <c>Skip</c>/<c>Take</c>.</summary>
    Task<IReadOnlyList<AuditEventView>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default);

    /// <summary>The total number of matching audit events (ignoring paging), for the viewer's paging.</summary>
    Task<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// The distinct action names present in the tenant's audit trail, ordered alphabetically — the source for
    /// the viewer's "action" filter. Tenant-scoped like every other read (ADR 0016); the audit action vocabulary
    /// is open and defined across features (each feature owns its <c>*AuditActions</c>), so the actual values that
    /// have occurred are the only complete, tenant-relevant set.
    /// </summary>
    Task<IReadOnlyList<string>> ListActionsAsync(CancellationToken cancellationToken = default);
}
