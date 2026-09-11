using Microsoft.EntityFrameworkCore;
using Npgsql;
using ZWarden.Application.Tenancy;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;
using ZWarden.TestSupport;

namespace ZWarden.IntegrationTests;

/// <summary>
/// F3A on the networked tier (trust-boundaries §6; ADR 0016): the first production migration applies on
/// a real PostgreSQL, and the tenant filter denies cross-tenant access there too - the boundary is
/// exercised against a real provider in v1.0, not deferred. Each test uses its own database on the
/// shared container so it never interferes with the others; EF creates and drops it.
/// </summary>
[ParallelLimiter<ContainerParallelLimit>]
public class PostgresTenantTests
{
    [ClassDataSource<PostgresFixture>(Shared = SharedType.PerAssembly)]
    public required PostgresFixture Postgres { get; init; }

    [Test]
    [Category("Networked")]
    [Timeout(300_000)]
    public async Task Migration_creates_the_tenants_table_and_bootstrap_seeds_on_postgres(CancellationToken cancellationToken)
    {
        string connectionString = UniqueDatabase();
        DbContextOptions options = new DbContextOptionsBuilder<ZWardenDbContext>()
            .UseZWardenProvider(ZWardenDbProvider.Postgres, connectionString)
            .Options;
        SingleTenantContext tenantContext = new();
        try
        {
            await using ZWardenDbContext db = new(options, tenantContext);
            await db.Database.MigrateAsync(cancellationToken);       // creates the db + applies InitialTenant
            await TenantBootstrapper.EnsureDefaultTenantAsync(db, cancellationToken);

            Tenant only = await db.Set<Tenant>().SingleAsync(cancellationToken);
            await Assert.That(only.Id).IsEqualTo(Tenant.DefaultId);
        }
        finally
        {
            await DropDatabaseAsync(connectionString);
        }
    }

    [Test]
    [Category("Networked")]
    [Timeout(300_000)]
    public async Task Cross_tenant_access_is_denied_on_postgres(CancellationToken cancellationToken)
    {
        TenantId tenantA = TenantId.New();
        TenantId tenantB = TenantId.New();
        string connectionString = UniqueDatabase();
        DbContextOptions options = new DbContextOptionsBuilder<TenantTestDbContext>()
            .UseZWardenProvider(ZWardenDbProvider.Postgres, connectionString)
            .Options;
        try
        {
            await using (TenantTestDbContext seed = new(options, new TestTenantContext(tenantA)))
            {
                await seed.Database.EnsureCreatedAsync(cancellationToken);
                seed.TenantWidgets.Add(new TenantWidget { Id = ServerId.New(), Name = "a1" });
                await seed.SaveChangesAsync(cancellationToken);
            }

            await using (TenantTestDbContext asB = new(options, new TestTenantContext(tenantB)))
            {
                // B cannot see A's row through the filter,
                await Assert.That(await asB.TenantWidgets.CountAsync(cancellationToken)).IsEqualTo(0);

                // and cannot insert into A's scope.
                asB.TenantWidgets.Add(new TenantWidget { Id = ServerId.New(), TenantId = tenantA, Name = "spoof" });
                await Assert.That(async () => await asB.SaveChangesAsync(cancellationToken))
                    .Throws<TenantScopeViolationException>();
            }
        }
        finally
        {
            await DropDatabaseAsync(connectionString);
        }
    }

    private string UniqueDatabase()
    {
        NpgsqlConnectionStringBuilder builder = new(Postgres.ConnectionString)
        {
            Database = $"zw_{Guid.NewGuid():N}",
        };
        return builder.ConnectionString;
    }

    private static async Task DropDatabaseAsync(string connectionString)
    {
        DbContextOptions options = new DbContextOptionsBuilder<ZWardenDbContext>()
            .UseZWardenProvider(ZWardenDbProvider.Postgres, connectionString)
            .Options;
        await using ZWardenDbContext db = new(options, new SingleTenantContext());
        await db.Database.EnsureDeletedAsync();
    }
}
