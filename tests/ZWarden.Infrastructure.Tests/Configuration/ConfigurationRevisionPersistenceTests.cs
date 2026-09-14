using Microsoft.EntityFrameworkCore;
using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Configuration;
using ZWarden.Infrastructure.Persistence;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Configuration;

/// <summary>
/// F20b (PR 2): the <see cref="ConfigurationRevision"/> aggregate (<c>cfg-</c>) is tenant-owned and read only
/// through the tenant filter (ADR 0016; trust-boundaries §9 rule 4). Its typed ids store as native uuid
/// (ADR 0004), the <see cref="PzConfigFile"/> stores by name, and the parsed-value snapshot + drift-baseline
/// hash round-trip verbatim (ADR 0011). Proven against a real SQLite database with a two-tenant fixture; the
/// same behaviours run on PostgreSQL on the networked tier.
/// </summary>
public class ConfigurationRevisionPersistenceTests
{
    private static readonly TenantId TenantA = TenantId.New();
    private static readonly TenantId TenantB = TenantId.New();
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);
    private const string Snapshot = "[[\"Zombies\",\"n:4:i\"]]";
    private const string Hash = "1111111111111111111111111111111111111111111111111111111111111111";

    [Test]
    public async Task A_revision_is_stamped_with_the_ambient_tenant_and_round_trips_its_snapshot()
    {
        await WithSqlite(async options =>
        {
            ServerId server = ServerId.New();
            UserId author = UserId.New();
            ConfigurationRevisionId id;

            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                ConfigurationRevisionRepository repo = new(asA);
                ConfigurationRevision revision =
                    ConfigurationRevision.Record(server, PzConfigFile.SandboxVars, Snapshot, Hash, Now, author);
                id = revision.Id;
                repo.Add(revision);
                await asA.SaveChangesAsync();
            }

            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                ConfigurationRevision? found = await new ConfigurationRevisionRepository(asA).FindByIdAsync(id);
                await Assert.That(found).IsNotNull();
                await Assert.That(found!.TenantId).IsEqualTo(TenantA);
                await Assert.That(found.ServerId).IsEqualTo(server);
                await Assert.That(found.File).IsEqualTo(PzConfigFile.SandboxVars);
                await Assert.That(found.CanonicalSnapshot).IsEqualTo(Snapshot);
                await Assert.That(found.SnapshotHash).IsEqualTo(Hash);
                await Assert.That(found.CreatedAt).IsEqualTo(Now);
                await Assert.That(found.CreatedByUserId).IsEqualTo(author);
            }

            await using (ZWardenDbContext asB = new(options, new TestTenantContext(TenantB)))
            {
                ConfigurationRevisionRepository repo = new(asB);
                await Assert.That(await repo.CountAsync()).IsEqualTo(0);
                await Assert.That(await repo.FindByIdAsync(id)).IsNull();
            }
        });
    }

    [Test]
    public async Task An_unattributed_capture_round_trips_a_null_author()
    {
        await WithSqlite(async options =>
        {
            ConfigurationRevisionId id;
            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                ConfigurationRevision revision =
                    ConfigurationRevision.Record(ServerId.New(), PzConfigFile.Ini, Snapshot, Hash, Now);
                id = revision.Id;
                asA.Set<ConfigurationRevision>().Add(revision);
                await asA.SaveChangesAsync();
            }

            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                ConfigurationRevision stored = (await new ConfigurationRevisionRepository(asA).FindByIdAsync(id))!;
                await Assert.That(stored.CreatedByUserId).IsNull();
            }
        });
    }

    [Test]
    public async Task FindLatest_returns_the_newest_revision_for_that_file()
    {
        await WithSqlite(async options =>
        {
            ServerId server = ServerId.New();

            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                ConfigurationRevisionRepository repo = new(asA);
                repo.Add(ConfigurationRevision.Record(server, PzConfigFile.SandboxVars, Snapshot, "aaaa", Now));
                repo.Add(ConfigurationRevision.Record(server, PzConfigFile.SandboxVars, Snapshot, "bbbb", Now.AddMinutes(5)));
                // A different file must not shadow the SandboxVars latest.
                repo.Add(ConfigurationRevision.Record(server, PzConfigFile.Ini, Snapshot, "cccc", Now.AddMinutes(10)));
                await asA.SaveChangesAsync();

                ConfigurationRevision? latest = await repo.FindLatestAsync(server, PzConfigFile.SandboxVars);
                await Assert.That(latest).IsNotNull();
                await Assert.That(latest!.SnapshotHash).IsEqualTo("bbbb");
                await Assert.That(latest.CreatedAt).IsEqualTo(Now.AddMinutes(5));
            }
        });
    }

    [Test]
    public async Task FindLatest_is_null_when_no_revision_exists_for_the_file()
    {
        await WithSqlite(async options =>
        {
            await using ZWardenDbContext asA = new(options, new TestTenantContext(TenantA));
            await Assert.That(await new ConfigurationRevisionRepository(asA)
                .FindLatestAsync(ServerId.New(), PzConfigFile.SpawnPoints)).IsNull();
        });
    }

    [Test]
    public async Task ListForFile_returns_the_file_history_newest_first()
    {
        await WithSqlite(async options =>
        {
            ServerId server = ServerId.New();

            await using ZWardenDbContext asA = new(options, new TestTenantContext(TenantA));
            ConfigurationRevisionRepository repo = new(asA);
            repo.Add(ConfigurationRevision.Record(server, PzConfigFile.Ini, Snapshot, "one", Now));
            repo.Add(ConfigurationRevision.Record(server, PzConfigFile.Ini, Snapshot, "two", Now.AddMinutes(5)));
            repo.Add(ConfigurationRevision.Record(server, PzConfigFile.SandboxVars, Snapshot, "other", Now.AddMinutes(9)));
            await asA.SaveChangesAsync();

            IReadOnlyList<ConfigurationRevision> history = await repo.ListForFileAsync(server, PzConfigFile.Ini);
            await Assert.That(history.Count).IsEqualTo(2);
            await Assert.That(history[0].SnapshotHash).IsEqualTo("two");
            await Assert.That(history[1].SnapshotHash).IsEqualTo("one");
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
