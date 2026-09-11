using Microsoft.EntityFrameworkCore;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Persistence;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Persistence;

/// <summary>
/// F3A S2/S5: the tenant filter and ownership interceptor prove cross-tenant isolation against a real
/// SQLite database and a two-tenant fixture (trust-boundaries §6; ADR 0016). Offline tier.
/// </summary>
public class TenantIsolationTests
{
    private static readonly TenantId TenantA = TenantId.New();
    private static readonly TenantId TenantB = TenantId.New();

    [Test]
    public async Task Insert_stamps_the_ambient_tenant_when_unset()
    {
        await WithSqlite(async options =>
        {
            var context = new TestTenantContext(TenantA);
            await using TenantTestDbContext db = new(options, context);
            db.TenantWidgets.Add(new TenantWidget { Id = ServerId.New(), Name = "alpha" });
            await db.SaveChangesAsync();

            TenantWidget stored = await db.TenantWidgets.SingleAsync();
            await Assert.That(stored.TenantId).IsEqualTo(TenantA);
        });
    }

    [Test]
    public async Task Each_tenant_reads_only_its_own_rows()
    {
        await WithSqlite(async options =>
        {
            await SeedAsync(options, TenantA, "a1");
            await SeedAsync(options, TenantA, "a2");
            await SeedAsync(options, TenantB, "b1");

            await using (TenantTestDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                await Assert.That(await asA.TenantWidgets.CountAsync()).IsEqualTo(2);
                await Assert.That(await asA.TenantWidgets.AllAsync(w => w.TenantId == TenantA)).IsTrue();
            }

            await using (TenantTestDbContext asB = new(options, new TestTenantContext(TenantB)))
            {
                await Assert.That(await asB.TenantWidgets.CountAsync()).IsEqualTo(1);
                await Assert.That((await asB.TenantWidgets.SingleAsync()).Name).IsEqualTo("b1");
            }
        });
    }

    [Test]
    public async Task Cross_tenant_insert_is_rejected()
    {
        await WithSqlite(async options =>
        {
            await using TenantTestDbContext asA = new(options, new TestTenantContext(TenantA));
            asA.TenantWidgets.Add(new TenantWidget { Id = ServerId.New(), TenantId = TenantB, Name = "spoofed" });

            await Assert.That(async () => await asA.SaveChangesAsync())
                .Throws<TenantScopeViolationException>();

            asA.ChangeTracker.Clear();
            await Assert.That(await asA.TenantWidgets.CountAsync()).IsEqualTo(0);
        });
    }

    [Test]
    public async Task Tenant_scope_is_immutable()
    {
        await WithSqlite(async options =>
        {
            ServerId id = await SeedAsync(options, TenantA, "a1");

            await using TenantTestDbContext asA = new(options, new TestTenantContext(TenantA));
            TenantWidget widget = await asA.TenantWidgets.SingleAsync(w => w.Id == id);
            asA.Entry(widget).Property(w => w.TenantId).CurrentValue = TenantB;

            await Assert.That(async () => await asA.SaveChangesAsync())
                .Throws<TenantScopeViolationException>();
        });
    }

    [Test]
    public async Task Cross_tenant_update_is_rejected()
    {
        await WithSqlite(async options =>
        {
            ServerId id = await SeedAsync(options, TenantA, "a1");

            // B force-attaches an A-owned row it can never have read through the filter.
            await using TenantTestDbContext asB = new(options, new TestTenantContext(TenantB));
            TenantWidget foreign = new() { Id = id, TenantId = TenantA, Name = "a1", Version = Guid.NewGuid() };
            asB.Attach(foreign);
            asB.Entry(foreign).Property(w => w.Name).CurrentValue = "hijacked";

            await Assert.That(async () => await asB.SaveChangesAsync())
                .Throws<TenantScopeViolationException>();
        });
    }

    [Test]
    public async Task A_tenant_owned_model_without_a_context_fails_closed_at_build()
    {
        DbContextOptions options = new DbContextOptionsBuilder<NoTenantContextTestDbContext>()
            .UseZWardenProvider(ZWardenDbProvider.Sqlite, "Data Source=:memory:")
            .Options;

        // Building the model (first Model access) throws before any database is touched.
        await using NoTenantContextTestDbContext db = new(options);
        await Assert.That(() => db.Model).Throws<InvalidOperationException>();
    }

    private static async Task<ServerId> SeedAsync(DbContextOptions options, TenantId tenant, string name)
    {
        ServerId id = ServerId.New();
        await using TenantTestDbContext db = new(options, new TestTenantContext(tenant));
        db.TenantWidgets.Add(new TenantWidget { Id = id, TenantId = tenant, Name = name });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task WithSqlite(Func<DbContextOptions, Task> body)
    {
        string file = Path.Combine(Path.GetTempPath(), $"zw-{Guid.NewGuid():N}.db");
        DbContextOptions options = new DbContextOptionsBuilder<TenantTestDbContext>()
            .UseZWardenProvider(ZWardenDbProvider.Sqlite, $"Data Source={file};Pooling=False")
            .Options;
        try
        {
            await using (TenantTestDbContext db = new(options, new TestTenantContext(TenantA)))
            {
                await db.Database.EnsureCreatedAsync();
            }

            await body(options);
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // Best effort - a lingering pooled handle is harmless.
            }
        }
    }
}
