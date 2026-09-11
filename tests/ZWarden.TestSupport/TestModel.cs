using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZWarden.Application.Tenancy;
using ZWarden.Domain;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.TestSupport;

/// <summary>
/// A test-only entity shared by the SQLite (offline) and PostgreSQL (networked) persistence tests.
/// F2 is infrastructure (Q4), so the context and its conventions are proven against this rather than
/// a production table; the first production migration is F3A's.
/// </summary>
public sealed class Widget : IVersioned
{
    public ServerId Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public Guid Version { get; set; }
}

public sealed class WidgetConfiguration : IEntityTypeConfiguration<Widget>
{
    public void Configure(EntityTypeBuilder<Widget> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Name).IsRequired();
    }
}

/// <summary>
/// Adds the test assembly's configurations to the ZWarden model on either provider. Since F4 the
/// context is always an Identity store mapping the tenant-owned <see cref="Infrastructure.Identity.ApplicationUser"/>,
/// so it always needs an <see cref="ITenantContext"/>; this benign scaffold defaults to the single-tenant
/// (default) context — <see cref="Widget"/> is not tenant-owned, so the filter never touches it, and the
/// typed-id / version conventions this proves are unchanged.
/// </summary>
public sealed class TestDbContext : ZWardenDbContext
{
    public TestDbContext(DbContextOptions options)
        : base(options, new SingleTenantContext())
    {
    }

    protected override IEnumerable<Assembly> ConfigurationAssemblies => [typeof(TestDbContext).Assembly];

    public DbSet<Widget> Widgets => Set<Widget>();
}
