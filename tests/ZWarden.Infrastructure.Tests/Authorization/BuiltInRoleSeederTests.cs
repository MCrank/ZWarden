using Microsoft.EntityFrameworkCore;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Persistence;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Authorization;

/// <summary>
/// F5 S9 (PR 2): built-in role seeding is idempotent and grants the bootstrap user the Tenant Owner role
/// (PRD 12A; ADR 0018). Offline tier.
/// </summary>
public class BuiltInRoleSeederTests
{
    private static readonly TenantId TenantA = TenantId.New();

    [Test]
    public async Task Seeds_every_built_in_role_once_and_is_idempotent()
    {
        await WithSqlite(async options =>
        {
            await using (ZWardenDbContext db = new(options, new TestTenantContext(TenantA)))
            {
                await BuiltInRoleSeeder.EnsureSeededAsync(db);
            }

            await using (ZWardenDbContext db = new(options, new TestTenantContext(TenantA)))
            {
                await BuiltInRoleSeeder.EnsureSeededAsync(db); // second boot
            }

            await using (ZWardenDbContext db = new(options, new TestTenantContext(TenantA)))
            {
                await Assert.That(await db.Set<Role>().CountAsync(r => r.BuiltIn != null))
                    .IsEqualTo(BuiltInRoles.All.Count);
            }
        });
    }

    [Test]
    public async Task Grants_the_bootstrap_user_the_tenant_owner_role_idempotently()
    {
        await WithSqlite(async options =>
        {
            UserId admin = UserId.New();

            await using (ZWardenDbContext db = new(options, new TestTenantContext(TenantA)))
            {
                await BuiltInRoleSeeder.EnsureSeededAsync(db, admin);
            }

            await using (ZWardenDbContext db = new(options, new TestTenantContext(TenantA)))
            {
                await BuiltInRoleSeeder.EnsureSeededAsync(db, admin); // second boot, no duplicate
            }

            await using (ZWardenDbContext db = new(options, new TestTenantContext(TenantA)))
            {
                List<RoleAssignment> assignments =
                    await db.Set<RoleAssignment>().Where(a => a.UserId == admin).ToListAsync();
                await Assert.That(assignments.Count).IsEqualTo(1);

                Role ownerRole = await db.Set<Role>().FirstAsync(r => r.BuiltIn == BuiltInRoleKind.TenantOwner);
                await Assert.That(assignments[0].RoleId).IsEqualTo(ownerRole.Id);
                await Assert.That(assignments[0].ServerId).IsNull();
            }
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
