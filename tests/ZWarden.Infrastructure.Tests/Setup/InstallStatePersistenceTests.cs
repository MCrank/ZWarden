using Microsoft.EntityFrameworkCore;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Setup;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Setup;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Setup;

/// <summary>
/// F33: the non-tenant-owned <see cref="InstallState"/> singleton (<c>ist-</c>) and the
/// <see cref="SetupState"/> seam over it. It round-trips its recorded <see cref="TlsMode"/> and completion
/// on a real SQLite database, and completion latches the process-wide <see cref="SetupCompletionSignal"/> so
/// the first-run gate short-circuits. Being unfiltered, it is visible from any tenant scope. The same
/// behaviours run on PostgreSQL on the networked tier.
/// </summary>
public class InstallStatePersistenceTests
{
    private static readonly TenantId TenantA = TenantId.New();
    private static readonly TenantId TenantB = TenantId.New();

    [Test]
    public async Task A_fresh_install_reports_incomplete_and_no_tls_mode()
    {
        await WithSqlite(async options =>
        {
            await using ZWardenDbContext db = new(options, new TestTenantContext(TenantA));
            SetupState setup = new(db, new SetupCompletionSignal(), TimeProvider.System);

            await Assert.That(await setup.IsSetupCompleteAsync()).IsFalse();
            await Assert.That(await setup.GetTlsModeAsync()).IsNull();
        });
    }

    [Test]
    public async Task Recording_a_tls_mode_persists_it_without_completing_setup()
    {
        await WithSqlite(async options =>
        {
            await using (ZWardenDbContext write = new(options, new TestTenantContext(TenantA)))
            {
                SetupState setup = new(write, new SetupCompletionSignal(), TimeProvider.System);
                await setup.RecordTlsModeAsync(TlsMode.Private);
            }

            await using ZWardenDbContext read = new(options, new TestTenantContext(TenantA));
            SetupState reloaded = new(read, new SetupCompletionSignal(), TimeProvider.System);
            await Assert.That(await reloaded.GetTlsModeAsync()).IsEqualTo(TlsMode.Private);
            await Assert.That(await reloaded.IsSetupCompleteAsync()).IsFalse();
        });
    }

    [Test]
    public async Task Marking_complete_persists_and_latches_the_signal()
    {
        await WithSqlite(async options =>
        {
            SetupCompletionSignal signal = new();
            await using (ZWardenDbContext write = new(options, new TestTenantContext(TenantA)))
            {
                SetupState setup = new(write, signal, TimeProvider.System);
                await setup.MarkSetupCompleteAsync();
            }

            // The in-process signal latched, so a fresh reader short-circuits to complete.
            await Assert.That(signal.IsComplete).IsTrue();

            await using ZWardenDbContext read = new(options, new TestTenantContext(TenantA));
            SetupState reloaded = new(read, new SetupCompletionSignal(), TimeProvider.System);
            await Assert.That(await reloaded.IsSetupCompleteAsync()).IsTrue();
        });
    }

    [Test]
    public async Task The_install_state_is_visible_across_tenant_scopes()
    {
        await WithSqlite(async options =>
        {
            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                SetupState setup = new(asA, new SetupCompletionSignal(), TimeProvider.System);
                await setup.RecordTlsModeAsync(TlsMode.Public);
                await setup.MarkSetupCompleteAsync();
            }

            // Not tenant-owned: a different tenant scope reads the same install-state record.
            await using ZWardenDbContext asB = new(options, new TestTenantContext(TenantB));
            SetupState fromB = new(asB, new SetupCompletionSignal(), TimeProvider.System);
            await Assert.That(await fromB.IsSetupCompleteAsync()).IsTrue();
            await Assert.That(await fromB.GetTlsModeAsync()).IsEqualTo(TlsMode.Public);
        });
    }

    [Test]
    public async Task The_bootstrapper_seeds_a_single_empty_row_idempotently()
    {
        await WithSqlite(async options =>
        {
            await using ZWardenDbContext db = new(options, new TestTenantContext(TenantA));

            db.Add(InstallState.CreateDefault());
            await db.SaveChangesAsync();

            int rows = await db.Set<InstallState>().CountAsync();
            await Assert.That(rows).IsEqualTo(1);

            InstallState stored = await db.Set<InstallState>().SingleAsync();
            await Assert.That(stored.Id).IsEqualTo(InstallState.DefaultId);
            await Assert.That(stored.IsSetupComplete).IsFalse();
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
