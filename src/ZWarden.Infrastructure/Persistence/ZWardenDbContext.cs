using System.Reflection;
using Microsoft.EntityFrameworkCore;
using ZWarden.Domain;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Ids;

namespace ZWarden.Infrastructure.Persistence;

/// <summary>
/// The ZWarden EF Core context. Provider-agnostic (ADR 0005) and extensible: later features add
/// entities by shipping <see cref="IEntityTypeConfiguration{TEntity}"/> implementations, discovered
/// from <see cref="ConfigurationAssemblies"/>. Two conventions run over the whole model - typed-id
/// value conversion (ADR 0004) and the <see cref="IVersioned"/> concurrency token (ADR 0005) - so
/// neither is ever hand-written per property.
/// </summary>
public class ZWardenDbContext : DbContext
{
    public ZWardenDbContext(DbContextOptions options)
        : base(options)
    {
    }

    /// <summary>Assemblies scanned for entity configurations. Override to add a feature's assembly.</summary>
    protected virtual IEnumerable<Assembly> ConfigurationAssemblies => [typeof(ZWardenDbContext).Assembly];

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

    private static void ApplyConventions(ModelBuilder modelBuilder)
    {
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
        }
    }
}
