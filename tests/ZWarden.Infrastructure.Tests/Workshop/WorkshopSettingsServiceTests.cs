using Microsoft.EntityFrameworkCore;
using ZWarden.Application.Authorization;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Security;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tests.Agents;
using ZWarden.Infrastructure.Workshop;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Workshop;

/// <summary>
/// F110 PR-B: the <see cref="WorkshopSettingsService"/> is authorized against <c>Tenant.Manage</c>
/// (fail-closed), audits the act (never the value), and stores the key only as an encrypted envelope
/// (ADR 0044/0015). Proven against a real SQLite database with a fake protector so the stored bytes can be
/// asserted to differ from the plaintext.
/// </summary>
public class WorkshopSettingsServiceTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly UserId Actor = UserId.New();
    private static readonly string ManagePermission = Permissions.TenantManage.Name;
    private const string ValidKey = "ABCDEF0123456789ABCDEF0123456789"; // 32 hex, a plausible Steam Web API key

    [Test]
    public async Task Set_denies_an_actor_without_Tenant_Manage_and_writes_nothing()
    {
        await WithSqlite(async options =>
        {
            await using ZWardenDbContext ctx = Context(options);
            CapturingAuditWriter audit = new();
            WorkshopSettingsService service = Service(ctx, audit, held: []);

            await Assert.That(async () => await service.SetApiKeyAsync(Actor, new SecretString(ValidKey)))
                .Throws<AuthorizationDeniedException>();
            await Assert.That(await new WorkshopIntegrationSettingsRepository(ctx).CountAsync()).IsEqualTo(0);
            await Assert.That(audit.Entries).IsEmpty();
        });
    }

    [Test]
    public async Task Set_encrypts_the_key_stores_the_envelope_audits_and_lights_up_search()
    {
        await WithSqlite(async options =>
        {
            await using ZWardenDbContext ctx = Context(options);
            CapturingAuditWriter audit = new();
            WorkshopSettingsService service = Service(ctx, audit, held: [ManagePermission]);

            await service.SetApiKeyAsync(Actor, new SecretString(ValidKey));

            await Assert.That(await service.IsSearchAvailableAsync()).IsTrue();

            var stored = await new WorkshopIntegrationSettingsRepository(ctx).GetAsync();
            await Assert.That(stored!.ProtectedApiKey).IsNotEqualTo(ValidKey); // never plaintext at rest
            await Assert.That(stored.ProtectedApiKey.Contains(ValidKey, StringComparison.Ordinal)).IsFalse();

            await Assert.That(audit.Actions).Contains(WorkshopAuditActions.KeyConfigured);
            // The audit detail names the act only, never the key value.
            await Assert.That(audit.Entries.All(e => e.Detail is null || !e.Detail.Contains(ValidKey, StringComparison.Ordinal))).IsTrue();
        });
    }

    [Test]
    public async Task Set_upserts_the_single_per_tenant_row()
    {
        await WithSqlite(async options =>
        {
            await using ZWardenDbContext ctx = Context(options);
            WorkshopSettingsService service = Service(ctx, new CapturingAuditWriter(), held: [ManagePermission]);

            await service.SetApiKeyAsync(Actor, new SecretString(ValidKey));
            await service.SetApiKeyAsync(Actor, new SecretString("FEDCBA9876543210FEDCBA9876543210"));

            await Assert.That(await new WorkshopIntegrationSettingsRepository(ctx).CountAsync()).IsEqualTo(1);
        });
    }

    [Test]
    public async Task Set_rejects_a_malformed_key_without_storing_or_auditing()
    {
        await WithSqlite(async options =>
        {
            await using ZWardenDbContext ctx = Context(options);
            CapturingAuditWriter audit = new();
            WorkshopSettingsService service = Service(ctx, audit, held: [ManagePermission]);

            await Assert.That(async () => await service.SetApiKeyAsync(Actor, new SecretString("too-short!!")))
                .Throws<ArgumentException>();
            await Assert.That(await new WorkshopIntegrationSettingsRepository(ctx).CountAsync()).IsEqualTo(0);
            await Assert.That(audit.Entries).IsEmpty();
        });
    }

    [Test]
    public async Task Clear_denies_an_actor_without_Tenant_Manage()
    {
        await WithSqlite(async options =>
        {
            await using ZWardenDbContext ctx = Context(options);
            WorkshopSettingsService service = Service(ctx, new CapturingAuditWriter(), held: []);

            await Assert.That(async () => await service.ClearApiKeyAsync(Actor))
                .Throws<AuthorizationDeniedException>();
        });
    }

    [Test]
    public async Task Clear_returns_to_keyless_mode_and_audits()
    {
        await WithSqlite(async options =>
        {
            await using ZWardenDbContext ctx = Context(options);
            CapturingAuditWriter audit = new();
            WorkshopSettingsService service = Service(ctx, audit, held: [ManagePermission]);

            await service.SetApiKeyAsync(Actor, new SecretString(ValidKey));
            await service.ClearApiKeyAsync(Actor);

            await Assert.That(await service.IsSearchAvailableAsync()).IsFalse();
            await Assert.That(audit.Actions).Contains(WorkshopAuditActions.KeyCleared);
        });
    }

    [Test]
    public async Task GetActiveApiKey_decrypts_the_stored_key_and_is_null_when_keyless()
    {
        await WithSqlite(async options =>
        {
            await using ZWardenDbContext ctx = Context(options);
            WorkshopSettingsService service = Service(ctx, new CapturingAuditWriter(), held: [ManagePermission]);

            await Assert.That(await service.GetActiveApiKeyAsync()).IsNull();

            await service.SetApiKeyAsync(Actor, new SecretString(ValidKey));

            SecretString? active = await service.GetActiveApiKeyAsync();
            await Assert.That(active.HasValue).IsTrue();
            await Assert.That(active!.Value.Reveal()).IsEqualTo(ValidKey);
        });
    }

    private static ZWardenDbContext Context(DbContextOptions options) => new(options, new TestTenantContext(Tenant));

    private static WorkshopSettingsService Service(ZWardenDbContext ctx, CapturingAuditWriter audit, string[] held)
        => new(
            ctx,
            new WorkshopIntegrationSettingsRepository(ctx),
            new StubPermissionChecker(held),
            audit,
            new FakeSecretProtector());

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

/// <summary>A reversible, non-cryptographic <see cref="ISecretProtector"/> for tests: the "envelope" is a
/// tagged transform of the plaintext, so a test can prove the stored value is not the plaintext yet still
/// round-trips through <c>UnprotectString</c>.</summary>
internal sealed class FakeSecretProtector : ISecretProtector
{
    private const string Tag = "fakeenc:";

    public string Protect(ReadOnlySpan<byte> plaintext) => Tag + Convert.ToBase64String(plaintext);

    public byte[] Unprotect(string envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return Convert.FromBase64String(envelope[Tag.Length..]);
    }

    public string ProtectString(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        return Tag + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plaintext));
    }

    public string UnprotectString(string envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(envelope[Tag.Length..]));
    }
}
