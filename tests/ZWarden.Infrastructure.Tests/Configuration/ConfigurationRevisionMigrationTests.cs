using Microsoft.EntityFrameworkCore;
using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Configuration;
using ZWarden.Infrastructure.Persistence;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Configuration;

/// <summary>
/// F20b (PR 2): the <c>AddConfigurationRevisions</c> migration. Applying the SQLite migration history with the
/// real <see cref="MigrationRunner"/> creates the <c>ConfigurationRevisions</c> table, and a revision round-trips
/// through the migrated (not <c>EnsureCreated</c>) schema — proving the hand-generated DDL matches the model.
/// Offline tier; the PostgreSQL migration is exercised on the networked tier.
/// </summary>
public class ConfigurationRevisionMigrationTests
{
    private static readonly TenantId Tenant = TenantId.New();

    [Test]
    public async Task Migrating_creates_the_configuration_revisions_table_and_a_revision_round_trips()
    {
        string file = Path.Combine(Path.GetTempPath(), $"zw-{Guid.NewGuid():N}.db");
        DbContextOptions options = new DbContextOptionsBuilder<ZWardenDbContext>()
            .UseZWardenProvider(ZWardenDbProvider.Sqlite, $"Data Source={file};Pooling=False")
            .Options;
        TestTenantContext tenantContext = new(Tenant);
        try
        {
            ConfigurationRevisionId id;
            await using (ZWardenDbContext db = new(options, tenantContext))
            {
                await MigrationRunner.EnsureMigratedAsync(db);

                long tableCount = await db.Database
                    .SqlQuery<long>($"SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'table' AND name = 'ConfigurationRevisions'")
                    .SingleAsync();
                await Assert.That(tableCount).IsEqualTo(1L);

                ConfigurationRevision revision = ConfigurationRevision.Record(
                    ServerId.New(), PzConfigFile.SandboxVars, "[[\"Zombies\",\"n:4:i\"]]", "abc123", DateTimeOffset.UnixEpoch);
                id = revision.Id;
                db.Set<ConfigurationRevision>().Add(revision);
                await db.SaveChangesAsync();
            }

            await using (ZWardenDbContext db = new(options, tenantContext))
            {
                ConfigurationRevision stored = (await new ConfigurationRevisionRepository(db).FindByIdAsync(id))!;
                await Assert.That(stored.File).IsEqualTo(PzConfigFile.SandboxVars);
                await Assert.That(stored.SnapshotHash).IsEqualTo("abc123");
            }
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
