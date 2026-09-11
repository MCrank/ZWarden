using Microsoft.EntityFrameworkCore;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Persistence;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Tenancy;

/// <summary>F3A S4: a tenant-scoped repository returns only the ambient tenant's rows and stamps the
/// tenant on add (ADR 0016; §9 rule 4). Offline tier.</summary>
public class TenantScopedRepositoryTests
{
    private static readonly TenantId TenantA = TenantId.New();
    private static readonly TenantId TenantB = TenantId.New();

    [Test]
    public async Task Repository_reads_only_the_ambient_tenants_rows()
    {
        string file = Path.Combine(Path.GetTempPath(), $"zw-{Guid.NewGuid():N}.db");
        DbContextOptions options = new DbContextOptionsBuilder<TenantTestDbContext>()
            .UseZWardenProvider(ZWardenDbProvider.Sqlite, $"Data Source={file};Pooling=False")
            .Options;
        try
        {
            await using (TenantTestDbContext seed = new(options, new TestTenantContext(TenantA)))
            {
                await seed.Database.EnsureCreatedAsync();
            }

            // Add under A through the repository: the tenant is stamped on save.
            await using (TenantTestDbContext db = new(options, new TestTenantContext(TenantA)))
            {
                TenantWidgetRepository repo = new(db);
                repo.Add(new TenantWidget { Id = ServerId.New(), Name = "a1" });
                repo.Add(new TenantWidget { Id = ServerId.New(), Name = "a2" });
                await db.SaveChangesAsync();
            }

            await using (TenantTestDbContext db = new(options, new TestTenantContext(TenantB)))
            {
                TenantWidgetRepository repo = new(db);
                repo.Add(new TenantWidget { Id = ServerId.New(), Name = "b1" });
                await db.SaveChangesAsync();
            }

            await using (TenantTestDbContext db = new(options, new TestTenantContext(TenantA)))
            {
                TenantWidgetRepository repo = new(db);
                await Assert.That(await repo.CountAsync()).IsEqualTo(2);
                IReadOnlyList<TenantWidget> rows = await repo.ListAsync();
                await Assert.That(rows.All(w => w.TenantId == TenantA)).IsTrue();
            }

            await using (TenantTestDbContext db = new(options, new TestTenantContext(TenantB)))
            {
                TenantWidgetRepository repo = new(db);
                await Assert.That(await repo.CountAsync()).IsEqualTo(1);
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
