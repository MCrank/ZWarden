using Microsoft.EntityFrameworkCore;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Identity;
using ZWarden.Infrastructure.Persistence;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Identity;

/// <summary>
/// F4 S1: <see cref="ApplicationUser"/> is a tenant-owned Identity entity. It rides the same tenant
/// filter and ownership interceptor as every other tenant-owned record (ADR 0016), proven here against
/// a real SQLite database and a two-tenant fixture (trust-boundaries §6). Offline tier.
/// </summary>
public class ApplicationUserTests
{
    private static readonly TenantId TenantA = TenantId.New();
    private static readonly TenantId TenantB = TenantId.New();

    [Test]
    public async Task A_user_is_stamped_with_the_ambient_tenant_and_is_scoped_by_it()
    {
        await WithSqlite(async options =>
        {
            Guid id;
            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                ApplicationUser user = new("alice") { Email = "alice@example.test" };
                id = user.Id;
                asA.Users.Add(user);
                await asA.SaveChangesAsync();

                ApplicationUser stored = await asA.Users.SingleAsync();
                // The ambient tenant is stamped on insert, never chosen by the caller.
                await Assert.That(stored.TenantId).IsEqualTo(TenantA);
                // The key is a native UUIDv7 Guid; the usr- typed id wraps it (ADR 0004/0014).
                await Assert.That(stored.UserId).IsEqualTo(UserId.FromGuid(id));
                await Assert.That(stored.UserId.ToString()).StartsWith("usr-");
            }

            // Tenant B's context cannot see tenant A's user - the filter is always evaluated.
            await using (ZWardenDbContext asB = new(options, new TestTenantContext(TenantB)))
            {
                await Assert.That(await asB.Users.CountAsync()).IsEqualTo(0);
            }
        });
    }

    [Test]
    public async Task A_users_tenant_scope_is_immutable()
    {
        await WithSqlite(async options =>
        {
            await using (ZWardenDbContext seed = new(options, new TestTenantContext(TenantA)))
            {
                seed.Users.Add(new ApplicationUser("bob"));
                await seed.SaveChangesAsync();
            }

            await using ZWardenDbContext asA = new(options, new TestTenantContext(TenantA));
            ApplicationUser user = await asA.Users.SingleAsync();
            asA.Entry(user).Property(nameof(ApplicationUser.TenantId)).CurrentValue = TenantB;

            await Assert.That(async () => await asA.SaveChangesAsync())
                .Throws<TenantScopeViolationException>();
        });
    }

    private static async Task WithSqlite(Func<DbContextOptions, Task> body)
    {
        string file = Path.Combine(Path.GetTempPath(), $"zw-{Guid.NewGuid():N}.db");
        DbContextOptions options = new DbContextOptionsBuilder<ZWardenDbContext>()
            .UseZWardenProvider(ZWardenDbProvider.Sqlite, $"Data Source={file};Pooling=False")
            .Options;
        try
        {
            await using (ZWardenDbContext db = new(options, new TestTenantContext(TenantA)))
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
