using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ZWarden.Application.Configuration;
using ZWarden.Application.Mods;
using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Mods;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Mods;
using ZWarden.Infrastructure.Operations;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Servers;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Mods;

/// <summary>
/// #290 D1/D2: hub events become background mod refreshes. A boot (start, restart, update, recreate) marks the
/// Server booted and queues a discovery now plus a follow-up after the post-boot delay; a config apply queues one
/// discovery; any other kind, a foreign Agent's report, or an unknown operation queues nothing. An Agent connecting
/// queues a discovery of its Servers. The kind is read from the persisted Operation, never the wire.
/// </summary>
public class ModRefreshTriggerTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Delay = TimeSpan.FromMinutes(3);

    [Test]
    [Arguments(OperationKind.StartServer)]
    [Arguments(OperationKind.RestartServer)]
    [Arguments(OperationKind.UpdateServer)]
    [Arguments(OperationKind.RecreateServer)]
    public async Task A_boot_marks_the_server_booted_and_queues_discovery_now_and_after_the_delay(OperationKind kind)
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId server = await SeedServerAsync(options, agent);
            OperationId operation = await SeedOperationAsync(options, agent, kind, server);
            ModStateRecorderTests.RecordingQueue scheduler = new();

            await using (ZWardenDbContext db = new(options, new TestTenantContext(Tenant)))
            {
                await Trigger(db, scheduler).OperationSucceededAsync(operation, agent);
            }

            ModRefreshRequest discover = new(Tenant, ModRefreshKind.DiscoverServer, Server: server);
            await Assert.That(scheduler.Requests).IsEquivalentTo([discover]);
            await Assert.That(scheduler.Delayed.Single()).IsEqualTo((discover, Delay));

            await using ZWardenDbContext read = new(options, new TestTenantContext(Tenant));
            ServerModState state = (await new ServerModStateRepository(read).FindAsync(server))!;
            await Assert.That(state.BootSnapshotPending).IsTrue();
            await Assert.That(state.BootedAt).IsEqualTo(Now);
        });
    }

    [Test]
    [Arguments(OperationKind.ConfigApply)]
    [Arguments(OperationKind.ConfigApplyRaw)]
    public async Task A_config_apply_queues_one_discovery_and_no_boot(OperationKind kind)
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId server = await SeedServerAsync(options, agent);
            OperationId operation = await SeedOperationAsync(options, agent, kind, server);
            ModStateRecorderTests.RecordingQueue scheduler = new();

            await using (ZWardenDbContext db = new(options, new TestTenantContext(Tenant)))
            {
                await Trigger(db, scheduler).OperationSucceededAsync(operation, agent);
            }

            await Assert.That(scheduler.Requests).IsEquivalentTo(
                [new ModRefreshRequest(Tenant, ModRefreshKind.DiscoverServer, Server: server)]);
            await Assert.That(scheduler.Delayed).IsEmpty();
            await using ZWardenDbContext read = new(options, new TestTenantContext(Tenant));
            await Assert.That(await new ServerModStateRepository(read).FindAsync(server)).IsNull();
        });
    }

    [Test]
    [Arguments(OperationKind.StopServer)]
    [Arguments(OperationKind.ModDiscovery)]
    [Arguments(OperationKind.Backup)]
    public async Task Other_kinds_queue_nothing(OperationKind kind)
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId server = await SeedServerAsync(options, agent);
            OperationId operation = await SeedOperationAsync(options, agent, kind, server);
            ModStateRecorderTests.RecordingQueue scheduler = new();

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            await Trigger(db, scheduler).OperationSucceededAsync(operation, agent);

            await Assert.That(scheduler.Requests).IsEmpty();
            await Assert.That(scheduler.Delayed).IsEmpty();
        });
    }

    [Test]
    public async Task A_report_from_an_agent_that_does_not_own_the_operation_queues_nothing()
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId server = await SeedServerAsync(options, agent);
            OperationId operation = await SeedOperationAsync(options, agent, OperationKind.RestartServer, server);
            ModStateRecorderTests.RecordingQueue scheduler = new();

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            await Trigger(db, scheduler).OperationSucceededAsync(operation, AgentId.New());

            await Assert.That(scheduler.Requests).IsEmpty();
            await Assert.That(await new ServerModStateRepository(db).FindAsync(server)).IsNull();
        });
    }

    [Test]
    public async Task An_unknown_operation_queues_nothing()
    {
        await WithSqlite(async options =>
        {
            ModStateRecorderTests.RecordingQueue scheduler = new();

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            await Trigger(db, scheduler).OperationSucceededAsync(OperationId.New(), AgentId.New());

            await Assert.That(scheduler.Requests).IsEmpty();
        });
    }

    [Test]
    public async Task An_agent_connecting_queues_a_discovery_of_its_servers()
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ModStateRecorderTests.RecordingQueue scheduler = new();

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            Trigger(db, scheduler).AgentConnected(agent);

            await Assert.That(scheduler.Requests).IsEquivalentTo(
                [new ModRefreshRequest(Tenant, ModRefreshKind.DiscoverAgentServers, Agent: agent)]);
        });
    }

    // ---- #291: a mod-list apply patches the cached lists before the operation is marked done --------------------

    [Test]
    public async Task A_succeeding_config_apply_writes_its_workshop_items_and_mods_into_the_cached_inventory()
    {
        // Back-to-back Installs: the second recomputes from the cache, so it must already see the first one's lists
        // (the follow-up discovery only lands later).
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId server = await SeedServerAsync(options, agent);
            OperationId operation = await SeedConfigApplyAsync(
                options, agent, server, PzConfigFile.Ini,
                new ConfigApplyEdit("WorkshopItems", ConfigEditKind.Text, "100;200"),
                new ConfigApplyEdit("Mods", ConfigEditKind.Text, "A;B"),
                new ConfigApplyEdit("PublicName", ConfigEditKind.Text, "x"));
            ModInventoryCache cache = new();
            cache.Record(Inventory(server, agent, ["100"], ["A"]));

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            await Trigger(db, new ModStateRecorderTests.RecordingQueue(), cache).RecordAppliedModListsAsync(operation, agent);

            ModInventory patched = cache.GetLatest(server, agent)!;
            await Assert.That(string.Join(";", patched.ConfiguredWorkshopIds)).IsEqualTo("100;200");
            await Assert.That(string.Join(";", patched.EnabledModIds)).IsEqualTo("A;B");
        });
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Another_file_or_a_foreign_agents_report_leaves_the_cache_alone(bool foreignAgent)
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId server = await SeedServerAsync(options, agent);
            OperationId operation = await SeedConfigApplyAsync(
                options, agent, server, foreignAgent ? PzConfigFile.Ini : PzConfigFile.SandboxVars,
                new ConfigApplyEdit("Mods", ConfigEditKind.Text, "Z"));
            ModInventoryCache cache = new();
            cache.Record(Inventory(server, agent, ["100"], ["A"]));

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            await Trigger(db, new ModStateRecorderTests.RecordingQueue(), cache)
                .RecordAppliedModListsAsync(operation, foreignAgent ? AgentId.New() : agent);

            await Assert.That(string.Join(";", cache.GetLatest(server, agent)!.EnabledModIds)).IsEqualTo("A");
        });
    }

    private static ModInventory Inventory(ServerId server, AgentId agent, string[] workshop, string[] mods) =>
        new(server, agent, InstalledItems: [], ConfiguredWorkshopIds: workshop, EnabledModIds: mods, Issues: [], ObservedAt: Now);

    private static async Task<OperationId> SeedConfigApplyAsync(
        DbContextOptions options, AgentId agent, ServerId serverId, PzConfigFile file, params ConfigApplyEdit[] edits)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
        Operation operation = Operation.Enqueue(
            agent, OperationKind.ConfigApply, isMutating: true, Guid.NewGuid().ToString("N"), Now, serverId,
            new ConfigApplyPayload(file, null, edits).ToJson());
        new OperationRepository(db).Add(operation);
        await db.SaveChangesAsync();
        return operation.Id;
    }

    private static ModRefreshTrigger Trigger(
        ZWardenDbContext db, IModRefreshScheduler scheduler, IModInventoryCache? cache = null) =>
        new(
            new OperationRepository(db),
            new ServerModStateRepository(db),
            db,
            scheduler,
            cache ?? new ModInventoryCache(),
            new TestTenantContext(Tenant),
            new FixedClock(Now),
            Options.Create(new ModRefreshOptions { PostBootRediscoverDelay = Delay }),
            NullLogger<ModRefreshTrigger>.Instance);

    private static async Task<ServerId> SeedServerAsync(DbContextOptions options, AgentId agent)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
        Server server = Server.Import(agent, ServerId.New(), "survivors", Now);
        new ServerRepository(db).Add(server);
        await db.SaveChangesAsync();
        return server.Id;
    }

    private static async Task<OperationId> SeedOperationAsync(
        DbContextOptions options, AgentId agent, OperationKind kind, ServerId serverId)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
        Operation operation = Operation.Enqueue(agent, kind, isMutating: true, Guid.NewGuid().ToString("N"), Now, serverId);
        new OperationRepository(db).Add(operation);
        await db.SaveChangesAsync();
        return operation.Id;
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
}
