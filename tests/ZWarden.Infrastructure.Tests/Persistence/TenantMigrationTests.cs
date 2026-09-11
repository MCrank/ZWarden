using Microsoft.EntityFrameworkCore;
using ZWarden.Application.Tenancy;
using ZWarden.Domain.Tenancy;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Tests.Persistence;

/// <summary>
/// F3A S7: the first production migration. Applying the SQLite migration history with the real
/// <see cref="MigrationRunner"/> creates the <c>Tenants</c> table, and the bootstrap seeds the default
/// tenant onto it (ADR 0016). Offline tier. The PostgreSQL migration is exercised on the networked tier.
/// </summary>
public class TenantMigrationTests
{
    [Test]
    public async Task Migrating_creates_the_tenants_table_and_bootstrap_seeds_the_default_tenant()
    {
        string file = Path.Combine(Path.GetTempPath(), $"zw-{Guid.NewGuid():N}.db");
        DbContextOptions options = new DbContextOptionsBuilder<ZWardenDbContext>()
            .UseZWardenProvider(ZWardenDbProvider.Sqlite, $"Data Source={file};Pooling=False")
            .Options;
        SingleTenantContext tenantContext = new();
        try
        {
            await using (ZWardenDbContext db = new(options, tenantContext))
            {
                await MigrationRunner.EnsureMigratedAsync(db);
                await TenantBootstrapper.EnsureDefaultTenantAsync(db);
            }

            await using (ZWardenDbContext db = new(options, tenantContext))
            {
                // The migration (not EnsureCreated) built the schema, and the row is on it.
                long tableCount = await db.Database
                    .SqlQuery<long>($"SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'table' AND name = 'Tenants'")
                    .SingleAsync();
                await Assert.That(tableCount).IsEqualTo(1L);

                Tenant only = await db.Set<Tenant>().SingleAsync();
                await Assert.That(only.Id).IsEqualTo(Tenant.DefaultId);
            }
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
            }
        }
    }
}
