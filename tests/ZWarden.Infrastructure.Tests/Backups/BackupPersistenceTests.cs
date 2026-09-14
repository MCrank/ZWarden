using Microsoft.EntityFrameworkCore;
using ZWarden.Domain.Backups;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Backups;
using ZWarden.Infrastructure.Persistence;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Backups;

/// <summary>
/// F24: the <see cref="Backup"/> aggregate (<c>bkp-</c>) is tenant-owned and read only through the tenant filter
/// (ADR 0016; trust-boundaries §9 rule 4). Its typed ids store as native uuid (ADR 0004), the
/// <see cref="BackupReason"/> stores by name, and the archive facts (locator, size, checksum, retention metadata)
/// round-trip. Proven against a real SQLite database with a two-tenant fixture; the same behaviours run on
/// PostgreSQL on the networked tier.
/// </summary>
public class BackupPersistenceTests
{
    private static readonly TenantId TenantA = TenantId.New();
    private static readonly TenantId TenantB = TenantId.New();
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task A_backup_is_stamped_with_the_ambient_tenant_and_scoped_by_it()
    {
        await WithSqlite(async options =>
        {
            ServerId server = ServerId.New();
            AgentId agent = AgentId.New();
            Backup backup = Backup.Record(server, agent, "world-1.tar.gz", 2048, "deadbeef", BackupReason.Manual, Now);

            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                BackupRepository repo = new(asA);
                repo.Add(backup);
                await asA.SaveChangesAsync();

                Backup? found = await repo.FindByIdAsync(backup.Id);
                await Assert.That(found).IsNotNull();
                await Assert.That(found!.TenantId).IsEqualTo(TenantA);
            }

            await using (ZWardenDbContext asB = new(options, new TestTenantContext(TenantB)))
            {
                BackupRepository repo = new(asB);
                await Assert.That(await repo.CountAsync()).IsEqualTo(0);
                await Assert.That(await repo.FindByIdAsync(backup.Id)).IsNull();
            }
        });
    }

    [Test]
    public async Task A_backup_round_trips_its_archive_facts_and_retention_metadata()
    {
        await WithSqlite(async options =>
        {
            ServerId server = ServerId.New();
            AgentId agent = AgentId.New();
            Backup backup = Backup.Record(
                server, agent, "world-20260914-100000.tar.gz", 987654, "abc123def456",
                BackupReason.PreOperation, Now, Now.AddDays(30));

            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                asA.Set<Backup>().Add(backup);
                await asA.SaveChangesAsync();
            }

            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                Backup stored = (await new BackupRepository(asA).FindByIdAsync(backup.Id))!;
                await Assert.That(stored.ServerId).IsEqualTo(server);
                await Assert.That(stored.AgentId).IsEqualTo(agent);
                await Assert.That(stored.ArchiveName).IsEqualTo("world-20260914-100000.tar.gz");
                await Assert.That(stored.SizeBytes).IsEqualTo(987654L);
                await Assert.That(stored.Sha256).IsEqualTo("abc123def456");
                await Assert.That(stored.Reason).IsEqualTo(BackupReason.PreOperation);
                await Assert.That(stored.CreatedAt).IsEqualTo(Now);
                await Assert.That(stored.ExpiresAt).IsEqualTo(Now.AddDays(30));
            }
        });
    }

    [Test]
    public async Task ListForServerAsync_returns_that_servers_backups_newest_first()
    {
        await WithSqlite(async options =>
        {
            ServerId server = ServerId.New();
            ServerId other = ServerId.New();
            AgentId agent = AgentId.New();

            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                BackupRepository repo = new(asA);
                repo.Add(Backup.Record(server, agent, "older.tar.gz", 1, "a", BackupReason.Manual, Now));
                repo.Add(Backup.Record(server, agent, "newer.tar.gz", 1, "b", BackupReason.Manual, Now.AddHours(1)));
                repo.Add(Backup.Record(other, agent, "elsewhere.tar.gz", 1, "c", BackupReason.Manual, Now));
                await asA.SaveChangesAsync();

                IReadOnlyList<Backup> forServer = await repo.ListForServerAsync(server);
                await Assert.That(forServer.Count).IsEqualTo(2);
                await Assert.That(forServer[0].ArchiveName).IsEqualTo("newer.tar.gz");
                await Assert.That(forServer[1].ArchiveName).IsEqualTo("older.tar.gz");
            }
        });
    }

    [Test]
    public async Task Remove_deletes_a_backup_record()
    {
        await WithSqlite(async options =>
        {
            ServerId server = ServerId.New();
            Backup backup = Backup.Record(server, AgentId.New(), "gone.tar.gz", 1, "a", BackupReason.Manual, Now);

            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                BackupRepository repo = new(asA);
                repo.Add(backup);
                await asA.SaveChangesAsync();
            }

            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                BackupRepository repo = new(asA);
                Backup stored = (await repo.FindByIdAsync(backup.Id))!;
                repo.Remove(stored);
                await asA.SaveChangesAsync();

                await Assert.That(await repo.FindByIdAsync(backup.Id)).IsNull();
            }
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
