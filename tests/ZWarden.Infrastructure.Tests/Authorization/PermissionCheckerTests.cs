using Microsoft.EntityFrameworkCore;
using ZWarden.Application.Authorization;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Persistence;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Authorization;

/// <summary>
/// F5 S4 (PR 1): the fail-closed decision service (ADR 0018). Resolves principal → tenant-scoped
/// assignments → roles → permissions, honouring tenant-wide vs server-scoped semantics, and denies on any
/// missing scope. Proven against a real SQLite database and the two-tenant fixture. Offline tier.
/// </summary>
public class PermissionCheckerTests
{
    private static readonly TenantId TenantA = TenantId.New();
    private static readonly TenantId TenantB = TenantId.New();

    [Test]
    public async Task A_tenant_wide_grant_allows_a_tenant_wide_permission_and_denies_ungranted()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            await SeedAssignmentAsync(options, TenantA, user, server: null, Permissions.RoleManage);

            await using ZWardenDbContext db = new(options, new TestTenantContext(TenantA));
            PermissionChecker checker = new(db, new TestTenantContext(TenantA));

            await Assert.That((await checker.EvaluateAsync(user, Permissions.RoleManage)).IsAllowed).IsTrue();
            await Assert.That((await checker.EvaluateAsync(user, Permissions.UserManage)).IsAllowed).IsFalse();
        });
    }

    [Test]
    public async Task A_server_scoped_grant_allows_only_that_server()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverX = ServerId.New();
            ServerId serverY = ServerId.New();
            await SeedAssignmentAsync(options, TenantA, user, serverX, Permissions.ServerStart);

            await using ZWardenDbContext db = new(options, new TestTenantContext(TenantA));
            PermissionChecker checker = new(db, new TestTenantContext(TenantA));

            await Assert.That((await checker.EvaluateAsync(user, Permissions.ServerStart, serverX)).IsAllowed).IsTrue();
            await Assert.That((await checker.EvaluateAsync(user, Permissions.ServerStart, serverY)).IsAllowed).IsFalse();
        });
    }

    [Test]
    public async Task A_tenant_wide_grant_covers_every_server_for_a_server_scoped_permission()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            await SeedAssignmentAsync(options, TenantA, user, server: null, Permissions.ServerStart);

            await using ZWardenDbContext db = new(options, new TestTenantContext(TenantA));
            PermissionChecker checker = new(db, new TestTenantContext(TenantA));

            await Assert.That((await checker.EvaluateAsync(user, Permissions.ServerStart, ServerId.New())).IsAllowed).IsTrue();
            await Assert.That((await checker.EvaluateAsync(user, Permissions.ServerStart, ServerId.New())).IsAllowed).IsTrue();
        });
    }

    [Test]
    public async Task A_server_scoped_permission_with_no_resource_fails_closed()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            await SeedAssignmentAsync(options, TenantA, user, server: null, Permissions.ServerStart);

            await using ZWardenDbContext db = new(options, new TestTenantContext(TenantA));
            PermissionChecker checker = new(db, new TestTenantContext(TenantA));

            // Even though the user has a tenant-wide grant, checking a server-scoped permission with no
            // server never widens - it fails closed.
            await Assert.That((await checker.EvaluateAsync(user, Permissions.ServerStart, server: null)).IsAllowed).IsFalse();
        });
    }

    [Test]
    public async Task A_server_scoped_assignment_does_not_confer_a_tenant_wide_permission()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverX = ServerId.New();
            // The role grants Role.Manage (tenant-wide), but the assignment is scoped to one server:
            // it must not hand the user tenant-wide power.
            await SeedAssignmentAsync(options, TenantA, user, serverX, Permissions.RoleManage);

            await using ZWardenDbContext db = new(options, new TestTenantContext(TenantA));
            PermissionChecker checker = new(db, new TestTenantContext(TenantA));

            await Assert.That((await checker.EvaluateAsync(user, Permissions.RoleManage)).IsAllowed).IsFalse();
        });
    }

    [Test]
    public async Task No_assignments_and_no_tenant_both_deny()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();

            // A user with no assignments is denied.
            await using (ZWardenDbContext db = new(options, new TestTenantContext(TenantA)))
            {
                PermissionChecker checker = new(db, new TestTenantContext(TenantA));
                await Assert.That((await checker.EvaluateAsync(user, Permissions.RoleManage)).IsAllowed).IsFalse();
            }

            // An unauthenticated principal (no ambient tenant) is denied without touching the store.
            await using (ZWardenDbContext db = new(options, new TestTenantContext(TenantA)))
            {
                PermissionChecker checker = new(db, new TestTenantContext());
                AuthorizationDecision decision = await checker.EvaluateAsync(user, Permissions.RoleManage);
                await Assert.That(decision.IsAllowed).IsFalse();
            }
        });
    }

    [Test]
    public async Task Another_tenants_grant_is_never_visible()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            // Grant Role.Manage to the same user id under tenant B.
            await SeedAssignmentAsync(options, TenantB, user, server: null, Permissions.RoleManage);

            // Evaluated in tenant A's context, the user holds nothing.
            await using ZWardenDbContext db = new(options, new TestTenantContext(TenantA));
            PermissionChecker checker = new(db, new TestTenantContext(TenantA));

            await Assert.That((await checker.EvaluateAsync(user, Permissions.RoleManage)).IsAllowed).IsFalse();
        });
    }

    private static async Task SeedAssignmentAsync(
        DbContextOptions options,
        TenantId tenant,
        UserId user,
        ServerId? server,
        PermissionDefinition permission)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(tenant));
        Role role = Role.CreateCustom(tenant, $"role-{Guid.NewGuid():N}");
        role.Grant(permission);
        db.Set<Role>().Add(role);
        db.Set<RoleAssignment>().Add(server is null
            ? RoleAssignment.TenantWide(tenant, user, role.Id)
            : RoleAssignment.ForServer(tenant, user, role.Id, server.Value));
        await db.SaveChangesAsync();
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
