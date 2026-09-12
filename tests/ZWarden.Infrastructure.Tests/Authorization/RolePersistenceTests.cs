using Microsoft.EntityFrameworkCore;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Persistence;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Authorization;

/// <summary>
/// F5 S3 (PR 1): the tenant-owned <see cref="Role"/> aggregate persists — its permission bundle
/// round-trips, the tenant filter isolates each tenant's roles, and role names are unique per tenant (not
/// globally). Proven against a real SQLite database and a two-tenant fixture (trust-boundaries §6). Offline tier.
/// </summary>
public class RolePersistenceTests
{
    private static readonly TenantId TenantA = TenantId.New();
    private static readonly TenantId TenantB = TenantId.New();

    [Test]
    public async Task A_role_round_trips_with_its_permission_bundle()
    {
        await WithSqlite(async options =>
        {
            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                asA.Set<Role>().Add(Role.FromBuiltIn(TenantA, BuiltInRoles.Moderator));
                await asA.SaveChangesAsync();
            }

            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                Role stored = await asA.Set<Role>().Include(r => r.Permissions).SingleAsync();

                await Assert.That(stored.TenantId).IsEqualTo(TenantA);
                await Assert.That(stored.BuiltIn).IsEqualTo(BuiltInRoleKind.Moderator);
                await Assert.That(stored.Permissions.Count).IsEqualTo(BuiltInRoles.Moderator.Permissions.Count);
                await Assert.That(stored.HasPermission(Permissions.ServerRestart.Name)).IsTrue();
                await Assert.That(stored.HasPermission(Permissions.ConsoleExecute.Name)).IsFalse();
            }
        });
    }

    [Test]
    public async Task Roles_are_scoped_to_the_ambient_tenant()
    {
        await WithSqlite(async options =>
        {
            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                asA.Set<Role>().Add(Role.CreateCustom(TenantA, "Ops"));
                await asA.SaveChangesAsync();
            }

            await using ZWardenDbContext asB = new(options, new TestTenantContext(TenantB));
            await Assert.That(await asB.Set<Role>().CountAsync()).IsEqualTo(0);
        });
    }

    [Test]
    public async Task Two_tenants_may_hold_a_same_named_role_but_one_tenant_may_not()
    {
        await WithSqlite(async options =>
        {
            // Tenant A and tenant B both name a role "Ops": allowed (unique per tenant, ADR 0018).
            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                asA.Set<Role>().Add(Role.CreateCustom(TenantA, "Ops"));
                await asA.SaveChangesAsync();
            }

            await using (ZWardenDbContext asB = new(options, new TestTenantContext(TenantB)))
            {
                asB.Set<Role>().Add(Role.CreateCustom(TenantB, "Ops"));
                await asB.SaveChangesAsync();
            }

            // The same tenant naming "Ops" twice violates the per-tenant unique index.
            await using ZWardenDbContext asAAgain = new(options, new TestTenantContext(TenantA));
            asAAgain.Set<Role>().Add(Role.CreateCustom(TenantA, "Ops"));
            await Assert.That(async () => await asAAgain.SaveChangesAsync()).Throws<DbUpdateException>();
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
            }
        }
    }
}
