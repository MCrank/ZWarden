using Microsoft.EntityFrameworkCore;
using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Configuration;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Servers;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Configuration;

/// <summary>
/// F20b PR-3: the completion-time revision recorder. It records the Agent-reported snapshot as a
/// <see cref="ConfigurationRevision"/> (the new drift baseline, ADR 0011) against a Server this tenant owns, and
/// no-ops for a Server it does not — mirroring the other completion-time reconcilers (trust-boundaries.md §3).
/// Proven against a real SQLite database.
/// </summary>
public class ConfigurationRevisionRecorderTests
{
    private static readonly TenantId TenantA = TenantId.New();
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
    private const string Snapshot = "[[\"Zombies\",\"n:1:i\"]]";
    private const string Hash = "abc0000000000000000000000000000000000000000000000000000000000000";

    [Test]
    public async Task RecordAsync_records_a_revision_for_an_owned_server()
    {
        await WithSqlite(async options =>
        {
            ServerId server = await SeedServerAsync(options);

            await using (ZWardenDbContext db = Context(options))
            {
                await RecorderOver(db).RecordAsync(server, PzConfigFile.SandboxVars, Snapshot, Hash);
            }

            await using (ZWardenDbContext db = Context(options))
            {
                ConfigurationRevision? latest = await new ConfigurationRevisionRepository(db)
                    .FindLatestAsync(server, PzConfigFile.SandboxVars);
                await Assert.That(latest).IsNotNull();
                await Assert.That(latest!.CanonicalSnapshot).IsEqualTo(Snapshot);
                await Assert.That(latest.SnapshotHash).IsEqualTo(Hash);
                await Assert.That(latest.TenantId).IsEqualTo(TenantA);
                // The completion path has no acting user, so the revision is unattributed.
                await Assert.That(latest.CreatedByUserId).IsNull();
            }
        });
    }

    [Test]
    public async Task RecordAsync_is_a_no_op_for_a_server_this_tenant_does_not_own()
    {
        await WithSqlite(async options =>
        {
            // No Server seeded: the recorder must not create a revision for an unknown/foreign Server.
            await using (ZWardenDbContext db = Context(options))
            {
                await RecorderOver(db).RecordAsync(ServerId.New(), PzConfigFile.Ini, Snapshot, Hash);
            }

            await using (ZWardenDbContext db = Context(options))
            {
                await Assert.That(await new ConfigurationRevisionRepository(db).CountAsync()).IsEqualTo(0);
            }
        });
    }

    private static ConfigurationRevisionRecorder RecorderOver(ZWardenDbContext db) =>
        new(db, new ServerRepository(db), new ConfigurationRevisionRepository(db), TimeProvider.System);

    private static ZWardenDbContext Context(DbContextOptions options) => new(options, new TestTenantContext(TenantA));

    private static async Task<ServerId> SeedServerAsync(DbContextOptions options)
    {
        await using ZWardenDbContext db = Context(options);
        Server server = Server.Import(AgentId.New(), ServerId.New(), "alpha", Now);
        db.Add(server);
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
            await using (ZWardenDbContext db = Context(options))
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
