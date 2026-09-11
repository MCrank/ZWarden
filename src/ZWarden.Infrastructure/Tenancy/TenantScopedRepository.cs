using Microsoft.EntityFrameworkCore;
using ZWarden.Application.Tenancy;
using ZWarden.Domain.Tenancy;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Infrastructure.Tenancy;

/// <summary>
/// Base for a tenant-scoped repository (ADR 0016). Its read surface is only the <i>filtered</i>
/// <see cref="DbSet{TEntity}"/> - it never calls <c>IgnoreQueryFilters()</c> - so every query it can
/// express is already scoped to the ambient tenant. Concrete repositories derive from this and add
/// entity-specific queries over <see cref="Entities"/>.
/// </summary>
/// <typeparam name="TEntity">The tenant-owned entity type.</typeparam>
public abstract class TenantScopedRepository<TEntity> : ITenantScopedRepository<TEntity>
    where TEntity : class, ITenantOwned
{
    protected TenantScopedRepository(ZWardenDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Context = context;
    }

    /// <summary>The owning context.</summary>
    protected ZWardenDbContext Context { get; }

    /// <summary>The tenant-filtered query root. Derived repositories build every read from this.</summary>
    protected IQueryable<TEntity> Entities => Context.Set<TEntity>();

    /// <inheritdoc />
    public async Task<IReadOnlyList<TEntity>> ListAsync(CancellationToken cancellationToken = default)
        => await Entities.ToListAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public Task<int> CountAsync(CancellationToken cancellationToken = default)
        => Entities.CountAsync(cancellationToken);

    /// <inheritdoc />
    public void Add(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        Context.Add(entity);
    }
}
