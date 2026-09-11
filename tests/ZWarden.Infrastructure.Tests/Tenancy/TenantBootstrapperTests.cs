using Microsoft.EntityFrameworkCore;
using ZWarden.Application.Tenancy;
using ZWarden.Domain.Tenancy;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Tests.Tenancy;

/// <summary>F3A S3: the single-tenant self-hosted bootstrap is idempotent and seeds the fixed default
/// tenant (ADR 0016). Offline tier.</summary>
public class TenantBootstrapperTests
{
    [Test]
    public async Task Bootstrap_seeds_exactly_one_default_tenant_and_is_idempotent()
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
                await db.Database.EnsureCreatedAsync();

                await TenantBootstrapper.EnsureDefaultTenantAsync(db);
                await TenantBootstrapper.EnsureDefaultTenantAsync(db); // second run is a no-op
            }

            await using (ZWardenDbContext db = new(options, tenantContext))
            {
                Tenant only = await db.Set<Tenant>().SingleAsync();
                await Assert.That(only.Id).IsEqualTo(Tenant.DefaultId);
                await Assert.That(only.Name).IsEqualTo(Tenant.DefaultName);
                await Assert.That(only.Version).IsNotEqualTo(Guid.Empty);
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
