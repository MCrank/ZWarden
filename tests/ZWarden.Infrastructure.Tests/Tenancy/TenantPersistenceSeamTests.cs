using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Tenancy;
using ZWarden.Domain.Tenancy;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Tests.Tenancy;

/// <summary>F3A S7: the persistence + tenant DI seam resolves a tenant-scoped context, migrates, and
/// bootstraps the default tenant - the shape the host will wire (ADR 0016). Offline tier.</summary>
public class TenantPersistenceSeamTests
{
    [Test]
    public async Task The_seam_resolves_a_tenant_scoped_context_migrates_and_bootstraps()
    {
        string file = Path.Combine(Path.GetTempPath(), $"zw-{Guid.NewGuid():N}.db");
        try
        {
            await using ServiceProvider provider = new ServiceCollection()
                .AddTenantFoundation()
                .AddZWardenPersistence(ZWardenDbProvider.Sqlite, $"Data Source={file};Pooling=False")
                .BuildServiceProvider();

            await provider.MigrateAndBootstrapDefaultTenantAsync();

            using IServiceScope scope = provider.CreateScope();
            ITenantContext tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            await Assert.That(tenantContext.CurrentTenantId).IsEqualTo(Tenant.DefaultId);

            ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
            Tenant only = await db.Set<Tenant>().SingleAsync();
            await Assert.That(only.Id).IsEqualTo(Tenant.DefaultId);
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
