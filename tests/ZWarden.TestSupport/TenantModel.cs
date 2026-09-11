using System.Reflection;
using Microsoft.EntityFrameworkCore;
using ZWarden.Application.Tenancy;
using ZWarden.Domain;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.TestSupport;

/// <summary>
/// A test-only <b>tenant-owned</b> entity, shared by the SQLite (offline) and PostgreSQL (networked)
/// isolation tests. F3A's deliverable is the tenant-filter <i>convention</i>, so — exactly as F2
/// proved its conventions against <see cref="Widget"/> — it is proven against this rather than a
/// production tenant-owned table (Server/Agent/User arrive in F14/F7/F4). It carries no
/// <c>IEntityTypeConfiguration</c> so it lands in a model only through an explicit
/// <see cref="TenantTestDbContext"/> DbSet, never leaking into the non-tenant <see cref="TestDbContext"/>.
/// </summary>
public sealed class TenantWidget : ITenantOwned, IVersioned
{
    public ServerId Id { get; init; }

    public TenantId TenantId { get; init; }

    public string Name { get; set; } = string.Empty;

    public Guid Version { get; set; }
}

/// <summary>
/// A switchable <see cref="ITenantContext"/> for the two-tenant fixture (trust-boundaries §6). Unlike a
/// production context it lets a test choose the ambient tenant, or clear it to exercise the fail-closed
/// path. No production code path can set a tenant this way — that is the point of the interface.
/// </summary>
public sealed class TestTenantContext : ITenantContext
{
    private TenantId? _current;

    public TestTenantContext(TenantId current) => _current = current;

    public TestTenantContext() => _current = null;

    public void SetTenant(TenantId tenant) => _current = tenant;

    public void ClearTenant() => _current = null;

    public bool HasCurrentTenant => _current is not null;

    public TenantId CurrentTenantId =>
        _current ?? throw new InvalidOperationException("No ambient tenant is resolved.");
}

/// <summary>
/// A tenant-aware context mapping <see cref="TenantWidget"/> (and the benign <see cref="Widget"/>), for
/// the isolation tests. Requires an <see cref="ITenantContext"/> — the fail-closed rule (ADR 0016) makes
/// a tenant-owned model unbuildable without one.
/// </summary>
public sealed class TenantTestDbContext : ZWardenDbContext
{
    public TenantTestDbContext(DbContextOptions options, ITenantContext tenantContext)
        : base(options, tenantContext)
    {
    }

    protected override IEnumerable<Assembly> ConfigurationAssemblies => [typeof(TenantTestDbContext).Assembly];

    public DbSet<TenantWidget> TenantWidgets => Set<TenantWidget>();

    public DbSet<Widget> Widgets => Set<Widget>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        // Declared inline (not an IEntityTypeConfiguration) so TenantWidget never leaks into the
        // non-tenant TestDbContext; the base then applies the typed-id, version, and tenant-filter
        // conventions over it.
        builder.Entity<TenantWidget>().HasKey(w => w.Id);
        builder.Entity<TenantWidget>().Property(w => w.Name).IsRequired();
        base.OnModelCreating(builder);
    }
}

/// <summary>
/// Maps a tenant-owned entity but uses the tenant-less base constructor, to prove the fail-closed
/// model build (ADR 0016): building this context's model throws because a tenant-owned entity has no
/// tenant context.
/// </summary>
public sealed class NoTenantContextTestDbContext : ZWardenDbContext
{
    public NoTenantContextTestDbContext(DbContextOptions options)
        : base(options)
    {
    }

    protected override IEnumerable<Assembly> ConfigurationAssemblies => [];

    public DbSet<TenantWidget> TenantWidgets => Set<TenantWidget>();
}

/// <summary>A concrete tenant-scoped repository over the test entity, proving the
/// <see cref="TenantScopedRepository{TEntity}"/> pattern (F3A S4).</summary>
public sealed class TenantWidgetRepository : TenantScopedRepository<TenantWidget>
{
    public TenantWidgetRepository(ZWardenDbContext context)
        : base(context)
    {
    }
}
