using ZWarden.Domain.Ids;

namespace ZWarden.Domain.Tenancy;

/// <summary>
/// Marks an entity as <b>tenant-owned</b>: it carries an immutable tenant scope (PRD 7A), and by
/// declaring this interface it opts automatically into the tenant filter (a global query filter on
/// every read) and the ownership interceptor (auto-stamp on insert, reject a cross-tenant or
/// scope-changing write). See ADR 0016. Implementers expose <see cref="TenantId"/> as init-only so
/// the scope is set once, at construction, and never changed.
/// </summary>
public interface ITenantOwned
{
    /// <summary>The owning <see cref="Ids.TenantId"/>. Immutable once persisted; assigned from the
    /// ambient tenant context on insert, never chosen by a browser.</summary>
    TenantId TenantId { get; }
}
