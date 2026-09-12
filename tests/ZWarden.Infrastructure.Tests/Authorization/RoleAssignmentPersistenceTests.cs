using Microsoft.EntityFrameworkCore;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Persistence;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Authorization;

/// <summary>
/// F5 S3 (PR 1): the <see cref="RoleAssignment"/> binding is tenant-owned and read only through the tenant
/// filter (ADR 0016; trust-boundaries §9 rule 4), and its optional server scope (a nullable typed id)
/// round-trips. Proven against a real SQLite database and a two-tenant fixture. Offline tier.
/// </summary>
public class RoleAssignmentPersistenceTests
{
    private static readonly TenantId TenantA = TenantId.New();
    private static readonly TenantId TenantB = TenantId.New();

    [Test]
    public async Task An_assignment_is_stamped_with_the_ambient_tenant_and_scoped_by_it()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                RoleAssignmentRepository repo = new(asA);
                // Tenant left unset so the ownership interceptor stamps the ambient tenant.
                repo.Add(new RoleAssignment { UserId = user, RoleId = RoleId.New() });
                await asA.SaveChangesAsync();

                RoleAssignment stored = (await repo.ListForUserAsync(user)).Single();
                await Assert.That(stored.TenantId).IsEqualTo(TenantA);
            }

            await using (ZWardenDbContext asB = new(options, new TestTenantContext(TenantB)))
            {
                RoleAssignmentRepository repo = new(asB);
                await Assert.That(await repo.CountAsync()).IsEqualTo(0);
                await Assert.That((await repo.ListForUserAsync(user)).Count).IsEqualTo(0);
            }
        });
    }

    [Test]
    public async Task An_assignments_tenant_scope_is_immutable()
    {
        await WithSqlite(async options =>
        {
            await using (ZWardenDbContext seed = new(options, new TestTenantContext(TenantA)))
            {
                seed.Set<RoleAssignment>().Add(new RoleAssignment { UserId = UserId.New(), RoleId = RoleId.New() });
                await seed.SaveChangesAsync();
            }

            await using ZWardenDbContext asA = new(options, new TestTenantContext(TenantA));
            RoleAssignment assignment = await asA.Set<RoleAssignment>().SingleAsync();
            asA.Entry(assignment).Property(nameof(RoleAssignment.TenantId)).CurrentValue = TenantB;

            await Assert.That(async () => await asA.SaveChangesAsync())
                .Throws<TenantScopeViolationException>();
        });
    }

    [Test]
    public async Task Server_scope_round_trips_and_tenant_wide_is_null()
    {
        await WithSqlite(async options =>
        {
            UserId scopedUser = UserId.New();
            UserId wideUser = UserId.New();
            ServerId server = ServerId.New();

            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                asA.Set<RoleAssignment>().Add(RoleAssignment.ForServer(TenantA, scopedUser, RoleId.New(), server));
                asA.Set<RoleAssignment>().Add(RoleAssignment.TenantWide(TenantA, wideUser, RoleId.New()));
                await asA.SaveChangesAsync();
            }

            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                RoleAssignmentRepository repo = new(asA);

                RoleAssignment scoped = (await repo.ListForUserAsync(scopedUser)).Single();
                await Assert.That(scoped.ServerId).IsEqualTo(server);
                await Assert.That(scoped.IsServerScoped).IsTrue();

                RoleAssignment wide = (await repo.ListForUserAsync(wideUser)).Single();
                await Assert.That(wide.ServerId).IsNull();
                await Assert.That(wide.IsServerScoped).IsFalse();
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
