using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ZWarden.Application.Mods;
using ZWarden.Application.Operations;
using ZWarden.Application.Workshop;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Mods;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Mods;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Servers;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Mods;

/// <summary>
/// #290: the background processor. A discovery request enqueues a system, non-mutating
/// <see cref="OperationKind.ModDiscovery"/> for the Server (or each of an Agent's Servers); a metadata refresh makes
/// one batched Steam call for the items whose details are missing or stale, applies what was found (with the guessed
/// mod ids parsed from the description), and leaves the rest untouched.
/// </summary>
public class ModRefreshProcessorTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task A_server_discovery_enqueues_a_system_non_mutating_mod_discovery()
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId server = await SeedServerAsync(options, agent);
            RecordingCoordinator coordinator = new();

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            await Processor(db, coordinator, new StubMetadata()).ProcessAsync(
                new ModRefreshRequest(Tenant, ModRefreshKind.DiscoverServer, Server: server), CancellationToken.None);

            EnqueueOperationRequest request = coordinator.Requests.Single();
            await Assert.That(request.Kind).IsEqualTo(OperationKind.ModDiscovery);
            await Assert.That(request.IsMutating).IsFalse();
            await Assert.That(request.ServerId).IsEqualTo(server);
            await Assert.That(request.AgentId).IsEqualTo(agent);
            await Assert.That(coordinator.Actors.Single()).IsNull();
        });
    }

    [Test]
    public async Task An_unknown_server_enqueues_nothing()
    {
        await WithSqlite(async options =>
        {
            RecordingCoordinator coordinator = new();

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            await Processor(db, coordinator, new StubMetadata()).ProcessAsync(
                new ModRefreshRequest(Tenant, ModRefreshKind.DiscoverServer, Server: ServerId.New()), CancellationToken.None);

            await Assert.That(coordinator.Requests).IsEmpty();
        });
    }

    [Test]
    public async Task An_agent_discovery_enqueues_one_per_server_it_owns()
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId first = await SeedServerAsync(options, agent);
            ServerId second = await SeedServerAsync(options, agent);
            await SeedServerAsync(options, AgentId.New());
            RecordingCoordinator coordinator = new();

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            await Processor(db, coordinator, new StubMetadata()).ProcessAsync(
                new ModRefreshRequest(Tenant, ModRefreshKind.DiscoverAgentServers, Agent: agent), CancellationToken.None);

            await Assert.That(coordinator.Requests.Select(r => r.ServerId!.Value)).IsEquivalentTo([first, second]);
        });
    }

    [Test]
    public async Task A_metadata_refresh_applies_steam_details_and_description_guesses_in_one_call()
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId server = await SeedServerAsync(options, agent);
            await SeedItemsAsync(options, server, "100", "200", "300");
            StubMetadata metadata = new(
                new WorkshopItemMetadata("100", true, "More Traits", "https://images.steam/mt.jpg", 42, Now.AddDays(-1),
                    "Mod ID: 1299328280/ToadTraits\nMod ID: ToadTraitsDynamic", ["Build 42"]),
                new WorkshopItemMetadata("200", true, "KillCount", Description: "no ids here"),
                WorkshopItemMetadata.NotFound("300"));

            await using (ZWardenDbContext db = new(options, new TestTenantContext(Tenant)))
            {
                await Processor(db, new RecordingCoordinator(), metadata).ProcessAsync(
                    new ModRefreshRequest(Tenant, ModRefreshKind.RefreshMetadata, Server: server), CancellationToken.None);
            }

            await Assert.That(metadata.Calls).IsEqualTo(1);
            await Assert.That(string.Join(",", metadata.LastIds)).IsEqualTo("100,200,300");
            await using ZWardenDbContext read = new(options, new TestTenantContext(Tenant));
            Dictionary<string, ServerWorkshopItem> items = (await new ServerWorkshopItemRepository(read).ListForServerAsync(server))
                .ToDictionary(i => i.WorkshopId);
            await Assert.That(items["100"].Title).IsEqualTo("More Traits");
            await Assert.That(string.Join(";", items["100"].GuessedModIds)).IsEqualTo("ToadTraits;ToadTraitsDynamic");
            await Assert.That(string.Join(";", items["100"].Tags)).IsEqualTo("Build 42");
            await Assert.That(items["100"].MetadataRefreshedAt).IsEqualTo(Now);
            await Assert.That(items["200"].GuessedModIds).IsEmpty();
            await Assert.That(items["200"].HasMetadata).IsTrue();
            // Not found (deleted, hidden, or Steam down): untouched, so it is retried next time.
            await Assert.That(items["300"].HasMetadata).IsFalse();
        });
    }

    [Test]
    public async Task Fresh_items_are_not_fetched_again()
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId server = await SeedServerAsync(options, agent);
            await using (ZWardenDbContext seed = new(options, new TestTenantContext(Tenant)))
            {
                ServerWorkshopItem fresh = ServerWorkshopItem.Track(server, "100");
                fresh.ApplyMetadata("Fresh", null, null, null, [], [], Now.AddMinutes(-5));
                seed.Add(fresh);
                await seed.SaveChangesAsync();
            }

            StubMetadata metadata = new();
            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            await Processor(db, new RecordingCoordinator(), metadata).ProcessAsync(
                new ModRefreshRequest(Tenant, ModRefreshKind.RefreshMetadata, Server: server), CancellationToken.None);

            await Assert.That(metadata.Calls).IsEqualTo(0);
        });
    }

    private static ModRefreshProcessor Processor(ZWardenDbContext db, RecordingCoordinator coordinator, StubMetadata metadata) =>
        new(
            new ServerRepository(db),
            new ServerWorkshopItemRepository(db),
            db,
            coordinator,
            metadata,
            new FixedClock(Now),
            Options.Create(new ModRefreshOptions()));

    private static async Task<ServerId> SeedServerAsync(DbContextOptions options, AgentId agent)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
        Server server = Server.Import(agent, ServerId.New(), $"srv-{Guid.NewGuid():N}", Now);
        new ServerRepository(db).Add(server);
        await db.SaveChangesAsync();
        return server.Id;
    }

    private static async Task SeedItemsAsync(DbContextOptions options, ServerId server, params string[] workshopIds)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
        foreach (string id in workshopIds)
        {
            db.Add(ServerWorkshopItem.Track(server, id));
        }

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

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingCoordinator : IOperationCoordinator
    {
        public List<EnqueueOperationRequest> Requests { get; } = [];

        public List<UserId?> Actors { get; } = [];

        public Task<Operation> EnqueueAsync(
            EnqueueOperationRequest request, UserId? actor = null, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            Actors.Add(actor);
            return Task.FromResult(Operation.Enqueue(
                request.AgentId, request.Kind, request.IsMutating, request.IdempotencyKey, Now, request.ServerId));
        }

        public Task<Operation> RequestCancellationAsync(
            OperationId operationId, UserId? actor = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubMetadata(params WorkshopItemMetadata[] items) : IWorkshopMetadataClient
    {
        public int Calls { get; private set; }

        public IReadOnlyList<string> LastIds { get; private set; } = [];

        public Task<IReadOnlyList<WorkshopItemMetadata>> GetItemsAsync(
            IReadOnlyList<string> workshopIds, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastIds = workshopIds;
            return Task.FromResult<IReadOnlyList<WorkshopItemMetadata>>([.. items.Where(i => workshopIds.Contains(i.WorkshopId))]);
        }

        public Task<IReadOnlyList<string>> GetCollectionItemIdsAsync(string collectionId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
