using Microsoft.EntityFrameworkCore;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Workshop;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Workshop;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Workshop;

/// <summary>
/// F110 PR-B: the <see cref="WorkshopIntegrationSettings"/> aggregate (<c>wis-</c>) is tenant-owned and read
/// only through the tenant filter (ADR 0016), and the stored key envelope round-trips verbatim (it is the
/// opaque <c>ISecretProtector</c> output — the database never sees plaintext, ADR 0015/0044). Proven against a
/// real SQLite database with a two-tenant fixture; the same behaviours run on PostgreSQL on the networked tier.
/// </summary>
public class WorkshopIntegrationSettingsPersistenceTests
{
    private static readonly TenantId TenantA = TenantId.New();
    private static readonly TenantId TenantB = TenantId.New();
    private const string Envelope = "v1|k1|c2FsdA==|bm9uY2U=|Y2lwaGVy|dGFn";

    [Test]
    public async Task Settings_are_stamped_with_the_ambient_tenant_and_round_trip_the_key_envelope()
    {
        await WithSqlite(async options =>
        {
            WorkshopIntegrationSettingsId id;

            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                WorkshopIntegrationSettingsRepository repo = new(asA);
                WorkshopIntegrationSettings settings = WorkshopIntegrationSettings.Create();
                settings.SetProtectedApiKey(Envelope);
                id = settings.Id;
                repo.Add(settings);
                await asA.SaveChangesAsync();
            }

            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                WorkshopIntegrationSettings? found = await new WorkshopIntegrationSettingsRepository(asA).GetAsync();
                await Assert.That(found).IsNotNull();
                await Assert.That(found!.Id).IsEqualTo(id);
                await Assert.That(found.TenantId).IsEqualTo(TenantA);
                await Assert.That(found.ProtectedApiKey).IsEqualTo(Envelope);
                await Assert.That(found.KeyConfigured).IsTrue();
            }

            await using (ZWardenDbContext asB = new(options, new TestTenantContext(TenantB)))
            {
                WorkshopIntegrationSettingsRepository repo = new(asB);
                await Assert.That(await repo.CountAsync()).IsEqualTo(0);
                await Assert.That(await repo.GetAsync()).IsNull();
            }
        });
    }

    [Test]
    public async Task GetAsync_is_null_for_a_tenant_that_never_configured_a_key()
    {
        await WithSqlite(async options =>
        {
            await using ZWardenDbContext asA = new(options, new TestTenantContext(TenantA));
            await Assert.That(await new WorkshopIntegrationSettingsRepository(asA).GetAsync()).IsNull();
        });
    }

    [Test]
    public async Task Clearing_the_key_round_trips_to_keyless_mode()
    {
        await WithSqlite(async options =>
        {
            WorkshopIntegrationSettingsId id;

            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                WorkshopIntegrationSettings settings = WorkshopIntegrationSettings.Create();
                settings.SetProtectedApiKey(Envelope);
                id = settings.Id;
                new WorkshopIntegrationSettingsRepository(asA).Add(settings);
                await asA.SaveChangesAsync();
            }

            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                WorkshopIntegrationSettings stored = (await new WorkshopIntegrationSettingsRepository(asA).GetAsync())!;
                stored.ClearApiKey();
                await asA.SaveChangesAsync();
            }

            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                WorkshopIntegrationSettings stored = (await new WorkshopIntegrationSettingsRepository(asA).GetAsync())!;
                await Assert.That(stored.Id).IsEqualTo(id);
                await Assert.That(stored.KeyConfigured).IsFalse();
                await Assert.That(stored.ProtectedApiKey).IsEqualTo(string.Empty);
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
