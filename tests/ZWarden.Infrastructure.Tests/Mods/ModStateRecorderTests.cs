using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ZWarden.Application.Mods;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Mods;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Mods;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Servers;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Mods;

/// <summary>
/// #290: a discovery result becomes persisted mod state. Configured lists are recorded (filling the booted-with
/// snapshot when a boot is pending); one item row per Workshop id that is configured, booted with or on disk, the
/// rest pruned; <c>mod.info</c> ids that fail <see cref="PzModId"/> dropped; an Agent that doesn't own the Server
/// ignored; and items lacking Steam details queue one metadata refresh. Real SQLite.
/// </summary>
public class ModStateRecorderTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task A_first_discovery_records_the_configured_lists_and_one_row_per_item()
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId server = await SeedServerAsync(options, agent);
            RecordingQueue queue = new();

            await using (ZWardenDbContext db = new(options, new TestTenantContext(Tenant)))
            {
                await Recorder(db, queue).RecordAsync(Inventory(server, agent,
                    workshop: ["100", "200"], enabled: ["A"],
                    installed: [Item("100", "A"), Item("300", "C")]));
            }

            await using ZWardenDbContext read = new(options, new TestTenantContext(Tenant));
            ServerModState state = (await new ServerModStateRepository(read).FindAsync(server))!;
            await Assert.That(string.Join(";", state.ConfiguredWorkshopIds)).IsEqualTo("100;200");
            await Assert.That(string.Join(";", state.ConfiguredModIds)).IsEqualTo("A");
            await Assert.That(state.ConfigObservedAt).IsEqualTo(Now);

            IReadOnlyList<ServerWorkshopItem> items = await new ServerWorkshopItemRepository(read).ListForServerAsync(server);
            await Assert.That(string.Join("|", items.Select(i => $"{i.WorkshopId}:{i.OnDisk}:{string.Join(",", i.ObservedModIds)}")))
                .IsEqualTo("100:True:A|200:False:|300:True:C");
        });
    }

    [Test]
    public async Task Discovery_records_each_items_installed_timeupdated()
    {
        // #275: the .acf timeupdated the Agent reports for the copy on disk is stored for the Update-ready compare.
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId server = await SeedServerAsync(options, agent);
            DateTimeOffset installed = Now.AddDays(-4);

            await using (ZWardenDbContext db = new(options, new TestTenantContext(Tenant)))
            {
                await Recorder(db, new RecordingQueue()).RecordAsync(Inventory(server, agent, ["100", "200"], ["A", "B"],
                    [Item("100", "A") with { InstalledUpdatedAt = installed }, Item("200", "B")]));
            }

            await using ZWardenDbContext read = new(options, new TestTenantContext(Tenant));
            IReadOnlyList<ServerWorkshopItem> items = await new ServerWorkshopItemRepository(read).ListForServerAsync(server);
            await Assert.That(items.Single(i => i.WorkshopId == "100").InstalledUpdatedAt).IsEqualTo(installed);
            await Assert.That(items.Single(i => i.WorkshopId == "200").InstalledUpdatedAt).IsNull();
        });
    }

    [Test]
    public async Task Items_lacking_steam_details_queue_one_metadata_refresh_for_the_server()
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId server = await SeedServerAsync(options, agent);
            RecordingQueue queue = new();

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            await Recorder(db, queue).RecordAsync(Inventory(server, agent, ["100"], [], [Item("100", "A")]));

            await Assert.That(queue.Requests).IsEquivalentTo(
                [new ModRefreshRequest(Tenant, ModRefreshKind.RefreshMetadata, Server: server)]);
        });
    }

    [Test]
    public async Task A_pending_boot_is_filled_by_the_next_discovery()
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId server = await SeedServerAsync(options, agent);
            await using (ZWardenDbContext seed = new(options, new TestTenantContext(Tenant)))
            {
                ServerModState pending = ServerModState.For(server);
                pending.MarkBooted(Now.AddSeconds(-30));
                seed.Add(pending);
                await seed.SaveChangesAsync();
            }

            await using (ZWardenDbContext db = new(options, new TestTenantContext(Tenant)))
            {
                await Recorder(db, new RecordingQueue()).RecordAsync(Inventory(server, agent, ["100"], ["A"], []));
            }

            await using ZWardenDbContext read = new(options, new TestTenantContext(Tenant));
            ServerModState state = (await new ServerModStateRepository(read).FindAsync(server))!;
            await Assert.That(state.HasBootSnapshot).IsTrue();
            await Assert.That(string.Join(";", state.BootedWorkshopIds)).IsEqualTo("100");
        });
    }

    [Test]
    public async Task Rows_neither_configured_booted_nor_on_disk_are_pruned_but_booted_ones_stay()
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId server = await SeedServerAsync(options, agent);
            await using (ZWardenDbContext seed = new(options, new TestTenantContext(Tenant)))
            {
                ServerModState state = ServerModState.For(server);
                state.MarkBooted(Now.AddMinutes(-10));
                state.ObserveConfig(["100", "200"], ["A", "B"], Now.AddMinutes(-9));
                seed.AddRange(state, ServerWorkshopItem.Track(server, "100"), ServerWorkshopItem.Track(server, "200"),
                    ServerWorkshopItem.Track(server, "400"));
                await seed.SaveChangesAsync();
            }

            // 200 was removed from config and its files deleted, but the server booted with it: it stays until the
            // next boot (RemovedOnRestart). 400 is nowhere any more: pruned.
            await using (ZWardenDbContext db = new(options, new TestTenantContext(Tenant)))
            {
                await Recorder(db, new RecordingQueue()).RecordAsync(Inventory(server, agent, ["100"], ["A"], [Item("100", "A")]));
            }

            await using ZWardenDbContext read = new(options, new TestTenantContext(Tenant));
            IReadOnlyList<ServerWorkshopItem> items = await new ServerWorkshopItemRepository(read).ListForServerAsync(server);
            await Assert.That(string.Join("|", items.Select(i => i.WorkshopId))).IsEqualTo("100|200");
        });
    }

    [Test]
    public async Task Mod_info_ids_that_fail_the_mod_id_rule_are_dropped()
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId server = await SeedServerAsync(options, agent);

            await using (ZWardenDbContext db = new(options, new TestTenantContext(Tenant)))
            {
                await Recorder(db, new RecordingQueue()).RecordAsync(
                    Inventory(server, agent, ["100"], [], [Item("100", "Good", "Bad;Id", "Also/Bad")]));
            }

            await using ZWardenDbContext read = new(options, new TestTenantContext(Tenant));
            ServerWorkshopItem item = (await new ServerWorkshopItemRepository(read).ListForServerAsync(server)).Single();
            await Assert.That(string.Join(",", item.ObservedModIds)).IsEqualTo("Good");
        });
    }

    [Test]
    public async Task Non_numeric_workshop_ids_in_config_get_no_row()
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId server = await SeedServerAsync(options, agent);

            await using (ZWardenDbContext db = new(options, new TestTenantContext(Tenant)))
            {
                await Recorder(db, new RecordingQueue()).RecordAsync(Inventory(server, agent, ["100", "junk"], [], []));
            }

            await using ZWardenDbContext read = new(options, new TestTenantContext(Tenant));
            IReadOnlyList<ServerWorkshopItem> items = await new ServerWorkshopItemRepository(read).ListForServerAsync(server);
            await Assert.That(string.Join("|", items.Select(i => i.WorkshopId))).IsEqualTo("100");
            // The configured list itself is config as read: kept verbatim.
            ServerModState state = (await new ServerModStateRepository(read).FindAsync(server))!;
            await Assert.That(string.Join(";", state.ConfiguredWorkshopIds)).IsEqualTo("100;junk");
        });
    }

    [Test]
    public async Task An_inventory_from_an_agent_that_does_not_own_the_server_records_nothing()
    {
        await WithSqlite(async options =>
        {
            ServerId server = await SeedServerAsync(options, AgentId.New());
            RecordingQueue queue = new();

            await using (ZWardenDbContext db = new(options, new TestTenantContext(Tenant)))
            {
                await Recorder(db, queue).RecordAsync(Inventory(server, AgentId.New(), ["100"], ["A"], [Item("100", "A")]));
            }

            await using ZWardenDbContext read = new(options, new TestTenantContext(Tenant));
            await Assert.That(await new ServerModStateRepository(read).FindAsync(server)).IsNull();
            await Assert.That(await new ServerWorkshopItemRepository(read).ListForServerAsync(server)).IsEmpty();
            await Assert.That(queue.Requests).IsEmpty();
        });
    }

    [Test]
    public async Task Oversized_config_lists_are_ignored_without_throwing()
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId server = await SeedServerAsync(options, agent);
            string[] huge = [new string('9', ServerModState.MaxEntryLength + 1)];

            await using (ZWardenDbContext db = new(options, new TestTenantContext(Tenant)))
            {
                await Recorder(db, new RecordingQueue()).RecordAsync(Inventory(server, agent, huge, [], []));
            }

            await using ZWardenDbContext read = new(options, new TestTenantContext(Tenant));
            await Assert.That(await new ServerModStateRepository(read).FindAsync(server)).IsNull();
        });
    }

    private static ModStateRecorder Recorder(ZWardenDbContext db, RecordingQueue queue) =>
        new(
            new ServerRepository(db),
            new ServerModStateRepository(db),
            new ServerWorkshopItemRepository(db),
            db,
            queue,
            new TestTenantContext(Tenant),
            new FixedClock(Now),
            Options.Create(new ModRefreshOptions()),
            NullLogger<ModStateRecorder>.Instance);

    private static ModInventory Inventory(
        ServerId server, AgentId agent, IReadOnlyList<string> workshop, IReadOnlyList<string> enabled,
        IReadOnlyList<InstalledWorkshopItem> installed) =>
        new(server, agent, installed, workshop, enabled, Issues: [], ObservedAt: Now.AddHours(-5));

    private static InstalledWorkshopItem Item(string workshopId, params string[] modIds) =>
        new(workshopId, [.. modIds.Select(id => new InstalledMod(id, null, null, null, null, [], [], []))]);

    private static async Task<ServerId> SeedServerAsync(DbContextOptions options, AgentId agent)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
        Server server = Server.Import(agent, ServerId.New(), "survivors", Now);
        new ServerRepository(db).Add(server);
        await db.SaveChangesAsync();
        return server.Id;
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

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    internal sealed class RecordingQueue : IModRefreshScheduler
    {
        public List<ModRefreshRequest> Requests { get; } = [];

        public List<(ModRefreshRequest Request, TimeSpan Delay)> Delayed { get; } = [];

        public void Enqueue(ModRefreshRequest request) => Requests.Add(request);

        public void EnqueueAfter(ModRefreshRequest request, TimeSpan delay) => Delayed.Add((request, delay));
    }
}
