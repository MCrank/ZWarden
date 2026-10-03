using Microsoft.EntityFrameworkCore;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Mods;
using ZWarden.Infrastructure.Mods;
using ZWarden.Infrastructure.Persistence;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Mods;

/// <summary>
/// #290: the installed-item records (<see cref="ServerWorkshopItem"/>) and per-Server mod lists
/// (<see cref="ServerModState"/>) are tenant-owned (ADR 0016), round-trip their ordered lists and UTC instants, and
/// allow one row per (Server, Workshop id). Proven on a real SQLite database with a two-tenant fixture.
/// </summary>
public class ModStatePersistenceTests
{
    private static readonly TenantId TenantA = TenantId.New();
    private static readonly TenantId TenantB = TenantId.New();
    private static readonly DateTimeOffset At = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task A_workshop_item_round_trips_its_details_and_ordered_lists_within_its_tenant()
    {
        await WithSqlite(async options =>
        {
            ServerId server = ServerId.New();

            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                ServerWorkshopItem item = ServerWorkshopItem.Track(server, "1299328280");
                item.ApplyMetadata("More Traits", "https://images.steam/mt.jpg", 42, At.AddDays(-1), ["Build 42", "Traits"],
                    [Id("ToadTraits"), Id("ToadTraitsDynamic")], At);
                item.ObserveDisk(onDisk: true, [Id("ToadTraitsDynamic"), Id("ToadTraits")], At.AddMinutes(1));
                new ServerWorkshopItemRepository(asA).Add(item);
                await asA.SaveChangesAsync();
            }

            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                ServerWorkshopItem found = (await new ServerWorkshopItemRepository(asA).ListForServerAsync(server)).Single();
                await Assert.That(found.TenantId).IsEqualTo(TenantA);
                await Assert.That(found.Title).IsEqualTo("More Traits");
                await Assert.That(found.SteamUpdatedAt).IsEqualTo(At.AddDays(-1));
                await Assert.That(found.MetadataRefreshedAt).IsEqualTo(At);
                await Assert.That(string.Join(";", found.Tags)).IsEqualTo("Build 42;Traits");
                await Assert.That(string.Join(";", found.GuessedModIds)).IsEqualTo("ToadTraits;ToadTraitsDynamic");
                await Assert.That(string.Join(";", found.ObservedModIds)).IsEqualTo("ToadTraitsDynamic;ToadTraits");
                await Assert.That(found.OnDisk).IsTrue();
            }

            await using (ZWardenDbContext asB = new(options, new TestTenantContext(TenantB)))
            {
                await Assert.That(await new ServerWorkshopItemRepository(asB).ListForServerAsync(server)).IsEmpty();
            }
        });
    }

    [Test]
    public async Task A_second_row_for_the_same_server_and_workshop_id_is_rejected()
    {
        await WithSqlite(async options =>
        {
            ServerId server = ServerId.New();
            await using ZWardenDbContext asA = new(options, new TestTenantContext(TenantA));
            ServerWorkshopItemRepository repo = new(asA);
            repo.Add(ServerWorkshopItem.Track(server, "100"));
            repo.Add(ServerWorkshopItem.Track(server, "100"));

            await Assert.That(async () => await asA.SaveChangesAsync()).Throws<DbUpdateException>();
        });
    }

    [Test]
    public async Task Mod_state_round_trips_configured_and_booted_with_lists_within_its_tenant()
    {
        await WithSqlite(async options =>
        {
            ServerId server = ServerId.New();

            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                ServerModState state = ServerModState.For(server);
                state.MarkBooted(At);
                state.ObserveConfig(["200", "100"], ["B", "A"], At.AddSeconds(5));
                state.ObserveConfig(["200"], ["B"], At.AddMinutes(5));
                new ServerModStateRepository(asA).Add(state);
                await asA.SaveChangesAsync();
            }

            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                ServerModState? found = await new ServerModStateRepository(asA).FindAsync(server);
                await Assert.That(found).IsNotNull();
                await Assert.That(found!.TenantId).IsEqualTo(TenantA);
                await Assert.That(string.Join(";", found.ConfiguredWorkshopIds)).IsEqualTo("200");
                await Assert.That(string.Join(";", found.BootedWorkshopIds)).IsEqualTo("200;100");
                await Assert.That(string.Join(";", found.BootedModIds)).IsEqualTo("B;A");
                await Assert.That(found.BootedAt).IsEqualTo(At);
                await Assert.That(found.HasBootSnapshot).IsTrue();
                await Assert.That(found.BootSnapshotPending).IsFalse();
            }

            await using (ZWardenDbContext asB = new(options, new TestTenantContext(TenantB)))
            {
                await Assert.That(await new ServerModStateRepository(asB).FindAsync(server)).IsNull();
            }
        });
    }

    [Test]
    public async Task Replacing_a_list_on_a_tracked_state_is_saved()
    {
        // The lists are replaced wholesale on each observation; EF must detect the change and persist it.
        await WithSqlite(async options =>
        {
            ServerId server = ServerId.New();
            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                ServerModState state = ServerModState.For(server);
                state.ObserveConfig(["100"], ["A"], At);
                new ServerModStateRepository(asA).Add(state);
                await asA.SaveChangesAsync();
            }

            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                ServerModState state = (await new ServerModStateRepository(asA).FindAsync(server))!;
                state.ObserveConfig(["100", "300"], ["A", "C"], At.AddMinutes(1));
                await asA.SaveChangesAsync();
            }

            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                ServerModState state = (await new ServerModStateRepository(asA).FindAsync(server))!;
                await Assert.That(string.Join(";", state.ConfiguredWorkshopIds)).IsEqualTo("100;300");
                await Assert.That(string.Join(";", state.ConfiguredModIds)).IsEqualTo("A;C");
            }
        });
    }

    private static PzModId Id(string value) =>
        PzModId.TryCreate(value, out PzModId id) ? id : throw new ArgumentException(value);

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
