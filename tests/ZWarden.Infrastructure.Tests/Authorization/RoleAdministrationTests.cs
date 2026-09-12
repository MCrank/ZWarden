using Microsoft.EntityFrameworkCore;
using ZWarden.Application.Authorization;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Persistence;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Authorization;

/// <summary>
/// F5 S5 (PR 1): custom-role administration is Role.Manage-gated with no self-escalation (PRD 12A; ADR
/// 0018). Proven against a real SQLite database. Offline tier.
/// </summary>
public class RoleAdministrationTests
{
    private static readonly TenantId TenantA = TenantId.New();

    [Test]
    public async Task Creating_a_role_requires_role_manage()
    {
        await WithSqlite(async options =>
        {
            UserId actor = UserId.New(); // holds nothing

            await using ZWardenDbContext db = new(options, new TestTenantContext(TenantA));
            RoleAdministrationService admin = NewAdmin(db);

            await Assert.That(async () =>
                    await admin.CreateRoleAsync(actor, "Ops", [Permissions.ServerView]))
                .Throws<AuthorizationDeniedException>();
        });
    }

    [Test]
    public async Task An_actor_cannot_grant_a_permission_they_do_not_hold()
    {
        await WithSqlite(async options =>
        {
            UserId actor = UserId.New();
            // The actor may manage roles but does not hold Server.Start.
            await GrantActorAsync(options, actor, Permissions.RoleManage, Permissions.ServerView);

            await using ZWardenDbContext db = new(options, new TestTenantContext(TenantA));
            RoleAdministrationService admin = NewAdmin(db);

            await Assert.That(async () =>
                    await admin.CreateRoleAsync(actor, "Ops", [Permissions.ServerStart]))
                .Throws<AuthorizationDeniedException>();
        });
    }

    [Test]
    public async Task An_authorized_actor_creates_a_role_that_takes_effect()
    {
        await WithSqlite(async options =>
        {
            UserId actor = UserId.New();
            UserId target = UserId.New();
            ServerId server = ServerId.New();
            await GrantActorAsync(options, actor, Permissions.RoleManage, Permissions.ServerStart);

            await using ZWardenDbContext db = new(options, new TestTenantContext(TenantA));
            RoleAdministrationService admin = NewAdmin(db);
            PermissionChecker checker = new(db, new TestTenantContext(TenantA));

            Role role = await admin.CreateRoleAsync(actor, "Starters", [Permissions.ServerStart]);
            db.Set<RoleAssignment>().Add(RoleAssignment.ForServer(TenantA, target, role.Id, server));
            await db.SaveChangesAsync();

            await Assert.That((await checker.EvaluateAsync(target, Permissions.ServerStart, server)).IsAllowed).IsTrue();
        });
    }

    [Test]
    public async Task Adjusting_a_role_changes_its_bundle()
    {
        await WithSqlite(async options =>
        {
            UserId actor = UserId.New();
            await GrantActorAsync(options, actor, Permissions.RoleManage, Permissions.ServerView, Permissions.ServerRestart);

            await using ZWardenDbContext db = new(options, new TestTenantContext(TenantA));
            RoleAdministrationService admin = NewAdmin(db);

            Role role = await admin.CreateRoleAsync(actor, "Ops", [Permissions.ServerView]);
            await admin.AdjustRoleAsync(actor, role.Id, [Permissions.ServerView, Permissions.ServerRestart]);

            Role reloaded = await db.Set<Role>().Include(r => r.Permissions).FirstAsync(r => r.Id == role.Id);
            await Assert.That(reloaded.HasPermission(Permissions.ServerRestart.Name)).IsTrue();
            await Assert.That(reloaded.Permissions.Count).IsEqualTo(2);
        });
    }

    [Test]
    public async Task Deleting_a_custom_role_removes_it_and_its_assignments()
    {
        await WithSqlite(async options =>
        {
            UserId actor = UserId.New();
            UserId target = UserId.New();
            await GrantActorAsync(options, actor, Permissions.RoleManage, Permissions.ServerView);

            await using ZWardenDbContext db = new(options, new TestTenantContext(TenantA));
            RoleAdministrationService admin = NewAdmin(db);

            Role role = await admin.CreateRoleAsync(actor, "Ops", [Permissions.ServerView]);
            db.Set<RoleAssignment>().Add(RoleAssignment.TenantWide(TenantA, target, role.Id));
            await db.SaveChangesAsync();

            await admin.DeleteRoleAsync(actor, role.Id);

            await Assert.That(await db.Set<Role>().CountAsync(r => r.Id == role.Id)).IsEqualTo(0);
            await Assert.That(await db.Set<RoleAssignment>().CountAsync(a => a.RoleId == role.Id)).IsEqualTo(0);
        });
    }

    [Test]
    public async Task A_built_in_role_cannot_be_deleted()
    {
        await WithSqlite(async options =>
        {
            UserId actor = UserId.New();
            await GrantActorAsync(options, actor, Permissions.RoleManage);

            RoleId builtInId;
            await using (ZWardenDbContext seed = new(options, new TestTenantContext(TenantA)))
            {
                Role moderator = Role.FromBuiltIn(TenantA, BuiltInRoles.Moderator);
                builtInId = moderator.Id;
                seed.Set<Role>().Add(moderator);
                await seed.SaveChangesAsync();
            }

            await using ZWardenDbContext db = new(options, new TestTenantContext(TenantA));
            RoleAdministrationService admin = NewAdmin(db);

            await Assert.That(async () => await admin.DeleteRoleAsync(actor, builtInId))
                .Throws<AuthorizationDeniedException>();
        });
    }

    private static RoleAdministrationService NewAdmin(ZWardenDbContext db) =>
        new(db, new PermissionChecker(db, new TestTenantContext(TenantA)), new TestTenantContext(TenantA));

    private static async Task GrantActorAsync(DbContextOptions options, UserId actor, params PermissionDefinition[] permissions)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(TenantA));
        Role role = Role.CreateCustom(TenantA, $"actor-role-{Guid.NewGuid():N}");
        foreach (PermissionDefinition permission in permissions)
        {
            role.Grant(permission);
        }

        db.Set<Role>().Add(role);
        db.Set<RoleAssignment>().Add(RoleAssignment.TenantWide(TenantA, actor, role.Id));
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
