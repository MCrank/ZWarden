using System.Reflection;
using Microsoft.EntityFrameworkCore;
using ZWarden.Application.Tenancy;
using ZWarden.Domain;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;
using ZWarden.Infrastructure.Ids;

namespace ZWarden.Infrastructure.Persistence;

/// <summary>
/// The ZWarden EF Core context. Provider-agnostic (ADR 0005) and extensible: later features add
/// entities by shipping <see cref="IEntityTypeConfiguration{TEntity}"/> implementations, discovered
/// from <see cref="ConfigurationAssemblies"/>. Three conventions run over the whole model - typed-id
/// value conversion (ADR 0004), the <see cref="IVersioned"/> concurrency token (ADR 0005), and the
/// tenant filter on every <see cref="ITenantOwned"/> entity (ADR 0016) - so none is ever hand-written
/// per property or per query.
/// </summary>
public class ZWardenDbContext : DbContext
{
    private readonly ITenantContext? _tenantContext;

    /// <summary>Constructs a context with no tenant scope. Valid only for a model that maps no
    /// <see cref="ITenantOwned"/> entity; a tenant-owned model built this way throws (fail closed).</summary>
    public ZWardenDbContext(DbContextOptions options)
        : base(options)
    {
    }

    /// <summary>Constructs a tenant-scoped context. The <paramref name="tenantContext"/> feeds the
    /// tenant filter and the ownership interceptor.</summary>
    public ZWardenDbContext(DbContextOptions options, ITenantContext tenantContext)
        : base(options)
    {
        ArgumentNullException.ThrowIfNull(tenantContext);
        _tenantContext = tenantContext;
    }

    /// <summary>Assemblies scanned for entity configurations. Override to add a feature's assembly.</summary>
    protected virtual IEnumerable<Assembly> ConfigurationAssemblies => [typeof(ZWardenDbContext).Assembly];

    /// <summary>
    /// The ambient tenant, read by the tenant filter and the ownership interceptor at query/save time.
    /// <b>Fails closed:</b> throws when the context has no <see cref="ITenantContext"/>, or when the
    /// context has one but no tenant is currently resolved (the latter propagates from
    /// <see cref="ITenantContext.CurrentTenantId"/>).
    /// </summary>
    internal TenantId CurrentTenantId =>
        _tenantContext is null
            ? throw new InvalidOperationException(
                "This ZWardenDbContext maps tenant-owned entities but was constructed without an ITenantContext.")
            : _tenantContext.CurrentTenantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        foreach (Assembly assembly in ConfigurationAssemblies)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(assembly);
        }

        ApplyConventions(modelBuilder);
    }

    private void ApplyConventions(ModelBuilder modelBuilder)
    {
        // A tenant-owned entity's TenantId is an unmapped struct until the converter pass below, so EF's
        // property discovery skips it. Declare it first so it becomes a mapped column and then receives
        // the typed-id converter - the tenant scope is a convention, never hand-declared per entity.
        foreach (Microsoft.EntityFrameworkCore.Metadata.IMutableEntityType tenantOwned in
                 modelBuilder.Model.GetEntityTypes()
                     .Where(e => typeof(ITenantOwned).IsAssignableFrom(e.ClrType))
                     .ToList())
        {
            modelBuilder.Entity(tenantOwned.ClrType).Property(nameof(ITenantOwned.TenantId));
        }

        foreach (Microsoft.EntityFrameworkCore.Metadata.IMutableEntityType entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (Microsoft.EntityFrameworkCore.Metadata.IMutableProperty property in entity.GetProperties())
            {
                if (property.ClrType.IsValueType && typeof(ITypedId).IsAssignableFrom(property.ClrType))
                {
                    property.SetValueConverter(TypedIdValueConverters.For(property.ClrType));
                }
            }

            if (typeof(IVersioned).IsAssignableFrom(entity.ClrType))
            {
                Microsoft.EntityFrameworkCore.Metadata.IMutableProperty? version =
                    entity.FindProperty(nameof(IVersioned.Version));
                if (version is not null)
                {
                    version.IsConcurrencyToken = true;
                }
            }

            if (typeof(ITenantOwned).IsAssignableFrom(entity.ClrType))
            {
                if (_tenantContext is null)
                {
                    throw new InvalidOperationException(
                        $"Entity '{entity.ClrType.Name}' is tenant-owned but this ZWardenDbContext was constructed " +
                        "without an ITenantContext; a tenant-owned model must fail closed (ADR 0016).");
                }

                ApplyTenantFilterMethod.MakeGenericMethod(entity.ClrType).Invoke(this, [modelBuilder]);
            }
        }
    }

    private static readonly MethodInfo ApplyTenantFilterMethod =
        typeof(ZWardenDbContext).GetMethod(nameof(ApplyTenantFilter), BindingFlags.Instance | BindingFlags.NonPublic)!;

    // The filter closes over this context's CurrentTenantId; EF re-evaluates it against the executing
    // context instance per query (the documented multitenancy pattern), so the cached model stays correct.
    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantOwned
        => modelBuilder.Entity<TEntity>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
}
