using Microsoft.EntityFrameworkCore;
using ZWarden.Application.Mods;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Mods;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Mods;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Servers;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Mods;

/// <summary>
/// #290: the read service the #292 Mods page will use. It returns the derived overview only to a caller holding
/// <c>Mod.View</c> on the Server (ADR 0018), and nothing for an unknown Server.
/// </summary>
public class ServerModOverviewServiceTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task A_mod_viewer_gets_the_derived_overview()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId server = await SeedServerAsync(options);
            await SeedAssignmentAsync(options, user, server, Permissions.ModView);
            await using (ZWardenDbContext seed = new(options, new TestTenantContext(Tenant)))
            {
                ServerModState state = ServerModState.For(server);
                state.MarkBooted(Now);
                state.ObserveConfig(["100"], ["A"], Now.AddSeconds(5));
                state.ObserveConfig(["100", "200"], ["A"], Now.AddMinutes(1));
                seed.AddRange(state, ServerWorkshopItem.Track(server, "100"), ServerWorkshopItem.Track(server, "200"));
                await seed.SaveChangesAsync();
            }

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerModOverview? overview = await Service(db).GetAsync(user, server);

            await Assert.That(overview).IsNotNull();
            await Assert.That(string.Join("|", overview!.Items.Select(i => $"{i.WorkshopId}={i.Status}")))
                .IsEqualTo("100=Active|200=InstallsOnRestart");
            await Assert.That(overview.PendingChanges).IsEqualTo(1);
        });
    }

    [Test]
    public async Task A_caller_without_mod_view_gets_nothing()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId server = await SeedServerAsync(options);
            await SeedAssignmentAsync(options, user, server, Permissions.ServerView);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            await Assert.That(await Service(db).GetAsync(user, server)).IsNull();
        });
    }

    [Test]
    public async Task An_unknown_server_gets_nothing()
    {
        await WithSqlite(async options =>
        {
            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            await Assert.That(await Service(db).GetAsync(UserId.New(), ServerId.New())).IsNull();
        });
    }

    private static ServerModOverviewService Service(ZWardenDbContext db) =>
        new(
            new ServerRepository(db),
            new PermissionChecker(db, new TestTenantContext(Tenant)),
            new ServerModStateRepository(db),
            new ServerWorkshopItemRepository(db));

    private static async Task<ServerId> SeedServerAsync(DbContextOptions options)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
        Server server = Server.Import(AgentId.New(), ServerId.New(), "survivors", Now);
        new ServerRepository(db).Add(server);
        await db.SaveChangesAsync();
        return server.Id;
    }

    private static async Task SeedAssignmentAsync(
        DbContextOptions options, UserId user, ServerId server, PermissionDefinition permission)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
        Role role = Role.CreateCustom(Tenant, $"role-{Guid.NewGuid():N}");
        role.Grant(permission);
        db.Set<Role>().Add(role);
        db.Set<RoleAssignment>().Add(RoleAssignment.ForServer(Tenant, user, role.Id, server));
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
            await using (ZWardenDbContext db = new(options, new TestTenantContext(Tenant)))
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
