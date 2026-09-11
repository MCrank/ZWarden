using ZWarden.Domain.Tenancy;

namespace ZWarden.Application.Tenancy;

/// <summary>
/// A repository over a tenant-owned entity whose every read is scoped to the ambient tenant (ADR 0016;
/// trust-boundaries §9 rule 4). It exposes <b>no unscoped read</b>: there is no member that returns rows
/// across tenants, and no implementation escapes the tenant filter. Writes are scoped by the ownership
/// interceptor.
/// </summary>
/// <typeparam name="TEntity">The tenant-owned entity type.</typeparam>
public interface ITenantScopedRepository<TEntity>
    where TEntity : class, ITenantOwned
{
    /// <summary>All of the ambient tenant's rows.</summary>
    Task<IReadOnlyList<TEntity>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>How many rows the ambient tenant owns.</summary>
    Task<int> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>Stages an insert; the ownership interceptor stamps the ambient tenant on save.</summary>
    void Add(TEntity entity);
}
