using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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

    [Test]
    public async Task Opening_the_mods_page_queues_a_short_max_age_refresh_for_a_mod_viewer()
    {
        // #275 D4: the page asks once on open; the processor only calls Steam when details are older than 30 min.
        await WithSqlite(async options =>
        {
            UserId viewer = UserId.New();
            UserId stranger = UserId.New();
            ServerId server = await SeedServerAsync(options);
            await SeedAssignmentAsync(options, viewer, server, Permissions.ModView);
            await SeedAssignmentAsync(options, stranger, server, Permissions.ServerView);
            ModStateRecorderTests.RecordingQueue queue = new();

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            await Service(db, queue).RequestUpdateCheckAsync(stranger, server);
            await Assert.That(queue.Requests).IsEmpty();

            await Service(db, queue).RequestUpdateCheckAsync(viewer, server);
            await Assert.That(queue.Requests).IsEquivalentTo(
                [new ModRefreshRequest(Tenant, ModRefreshKind.RefreshMetadata, Server: server, MaxAge: new ModRefreshOptions().UpdateCheckMaxAge)]);
        });
    }

    [Test]
    public async Task The_fleet_gets_each_viewable_servers_count_of_updates_ready()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId updatable = await SeedServerAsync(options);
            ServerId current = await SeedServerAsync(options);
            ServerId hidden = await SeedServerAsync(options);
            await SeedAssignmentAsync(options, user, updatable, Permissions.ModView);
            await SeedAssignmentAsync(options, user, current, Permissions.ModView);
            await SeedAssignmentAsync(options, user, hidden, Permissions.ServerView);
            await using (ZWardenDbContext seed = new(options, new TestTenantContext(Tenant)))
            {
                foreach (ServerId server in new[] { updatable, current, hidden })
                {
                    ServerModState state = ServerModState.For(server);
                    state.MarkBooted(Now);
                    state.ObserveConfig(["100", "200"], ["A", "B"], Now.AddSeconds(5));
                    seed.Add(state);
                    seed.Add(Versions(server, "100", steam: server == current ? Now.AddDays(-5) : Now.AddDays(-1)));
                    seed.Add(Versions(server, "200", steam: Now.AddDays(-5)));
                }

                await seed.SaveChangesAsync();
            }

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            IReadOnlyDictionary<ServerId, int> counts =
                await Service(db).CountUpdatesReadyAsync(user, [updatable, current, hidden]);

            await Assert.That(counts[updatable]).IsEqualTo(1);
            await Assert.That(counts[current]).IsEqualTo(0);
            await Assert.That(counts.ContainsKey(hidden)).IsFalse();
        });
    }

    // On disk since Now - 5 days; Steam's version is `steam`.
    private static ServerWorkshopItem Versions(ServerId server, string workshopId, DateTimeOffset steam)
    {
        ServerWorkshopItem item = ServerWorkshopItem.Track(server, workshopId);
        item.ApplyMetadata("t", null, null, steam, [], [], Now);
        item.ObserveDisk(onDisk: true, [], Now, installedUpdatedAt: Now.AddDays(-5));
        return item;
    }

    private static ServerModOverviewService Service(ZWardenDbContext db, IModRefreshScheduler? scheduler = null) =>
        new(
            new ServerRepository(db),
            new PermissionChecker(db, new TestTenantContext(Tenant)),
            new ServerModStateRepository(db),
            new ServerWorkshopItemRepository(db),
            scheduler ?? new ModStateRecorderTests.RecordingQueue(),
            new TestTenantContext(Tenant),
            Options.Create(new ModRefreshOptions()));

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
