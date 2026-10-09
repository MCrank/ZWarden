using Microsoft.EntityFrameworkCore;
using ZWarden.Application.Authorization;
using ZWarden.Application.Settings;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Settings;
using ZWarden.Infrastructure.Tests.Agents;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Settings;

/// <summary>
/// #345: the <see cref="ControlPlaneSettingsService"/> keeps one settings row per tenant, authorizes every change
/// against <c>Tenant.Settings.Manage</c> (fail-closed), audits old → new, skips a no-op, and refreshes the in-process
/// cache the shell reads. Proven against a real SQLite database.
/// </summary>
public sealed class ControlPlaneSettingsServiceTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly UserId Actor = UserId.New();
    private static readonly string Manage = Permissions.TenantSettingsManage.Name;

    [Test]
    public async Task Nothing_is_overridden_until_a_value_is_set()
    {
        await WithSqlite(async options =>
        {
            await using ZWardenDbContext ctx = Context(options);
            ControlPlaneSettingsService service = Service(ctx, new CapturingAuditWriter(), new RecordingCache(), held: [Manage]);

            await Assert.That((await service.GetAsync()).InstanceName).IsNull();
        });
    }

    [Test]
    public async Task Setting_the_name_stores_it_audits_old_to_new_and_refreshes_the_cache()
    {
        await WithSqlite(async options =>
        {
            await using ZWardenDbContext ctx = Context(options);
            CapturingAuditWriter audit = new();
            RecordingCache cache = new();
            ControlPlaneSettingsService service = Service(ctx, audit, cache, held: [Manage]);

            await service.SetInstanceNameAsync(Actor, "  Knox Ops ");
            await service.SetInstanceNameAsync(Actor, "Muldraugh Ops");

            await Assert.That((await service.GetAsync()).InstanceName).IsEqualTo("Muldraugh Ops");
            await Assert.That(await new ControlPlaneSettingsRepository(ctx).CountAsync()).IsEqualTo(1);
            await Assert.That(audit.Actions).IsEquivalentTo([SettingsAuditActions.InstanceNameChanged, SettingsAuditActions.InstanceNameChanged]);
            await Assert.That(audit.Entries[0].Detail).IsEqualTo("instance name: (config) → \"Knox Ops\"");
            await Assert.That(audit.Entries[1].Detail).IsEqualTo("instance name: \"Knox Ops\" → \"Muldraugh Ops\"");
            await Assert.That(audit.Entries[1].ActorUserId).IsEqualTo(Actor);
            await Assert.That(cache.Last).IsEqualTo((Tenant, new ControlPlaneSettingsSnapshot("Muldraugh Ops")));
        });
    }

    [Test]
    public async Task Clearing_the_name_reverts_to_config_and_is_audited()
    {
        await WithSqlite(async options =>
        {
            await using ZWardenDbContext ctx = Context(options);
            CapturingAuditWriter audit = new();
            RecordingCache cache = new();
            ControlPlaneSettingsService service = Service(ctx, audit, cache, held: [Manage]);
            await service.SetInstanceNameAsync(Actor, "Knox Ops");

            await service.SetInstanceNameAsync(Actor, "   ");

            await Assert.That((await service.GetAsync()).InstanceName).IsNull();
            await Assert.That(audit.Entries[^1].Detail).IsEqualTo("instance name: \"Knox Ops\" → (config)");
            await Assert.That(cache.Last).IsEqualTo((Tenant, ControlPlaneSettingsSnapshot.Empty));
        });
    }

    [Test]
    public async Task An_unchanged_name_writes_and_audits_nothing()
    {
        await WithSqlite(async options =>
        {
            await using ZWardenDbContext ctx = Context(options);
            CapturingAuditWriter audit = new();
            ControlPlaneSettingsService service = Service(ctx, audit, new RecordingCache(), held: [Manage]);
            await service.SetInstanceNameAsync(Actor, "Knox Ops");

            await service.SetInstanceNameAsync(Actor, " Knox Ops");
            await service.SetInstanceNameAsync(Actor, null); // clear …
            await service.SetInstanceNameAsync(Actor, "");   // … and clear again: a no-op

            await Assert.That(audit.Entries.Count).IsEqualTo(2);
        });
    }

    [Test]
    public async Task An_actor_without_Tenant_Settings_Manage_is_denied_and_nothing_changes()
    {
        await WithSqlite(async options =>
        {
            await using ZWardenDbContext ctx = Context(options);
            CapturingAuditWriter audit = new();
            RecordingCache cache = new();
            ControlPlaneSettingsService service = Service(ctx, audit, cache, held: [Permissions.UserManage.Name]);

            await Assert.That(async () => await service.SetInstanceNameAsync(Actor, "Knox Ops"))
                .Throws<AuthorizationDeniedException>();
            await Assert.That(await new ControlPlaneSettingsRepository(ctx).CountAsync()).IsEqualTo(0);
            await Assert.That(audit.Entries).IsEmpty();
            await Assert.That(cache.Last).IsNull();
        });
    }

    [Test]
    public async Task An_invalid_name_is_refused_before_anything_changes()
    {
        await WithSqlite(async options =>
        {
            await using ZWardenDbContext ctx = Context(options);
            CapturingAuditWriter audit = new();
            ControlPlaneSettingsService service = Service(ctx, audit, new RecordingCache(), held: [Manage]);

            await Assert.That(async () => await service.SetInstanceNameAsync(Actor, new string('x', 65)))
                .Throws<ArgumentException>();
            await Assert.That(await new ControlPlaneSettingsRepository(ctx).CountAsync()).IsEqualTo(0);
            await Assert.That(audit.Entries).IsEmpty();
        });
    }

    [Test]
    public async Task The_owner_sets_the_session_timeout_and_it_is_audited_and_cached()
    {
        await WithSqlite(async options =>
        {
            await using ZWardenDbContext ctx = Context(options);
            CapturingAuditWriter audit = new();
            RecordingCache cache = new();
            ControlPlaneSettingsService service = Service(ctx, audit, cache, held: [Permissions.TenantManage.Name]);

            await service.SetSessionIdleTimeoutAsync(Actor, TimeSpan.FromMinutes(30));
            await service.SetSessionIdleTimeoutAsync(Actor, TimeSpan.FromHours(8)); // the default: stores nothing

            await Assert.That((await service.GetAsync()).SessionIdleTimeout).IsNull();
            await Assert.That(audit.Actions).IsEquivalentTo([SettingsAuditActions.SessionTimeoutChanged, SettingsAuditActions.SessionTimeoutChanged]);
            await Assert.That(audit.Entries[0].Detail).IsEqualTo("session timeout: 8 hours (default) → 30 minutes");
            await Assert.That(audit.Entries[1].Detail).IsEqualTo("session timeout: 30 minutes → 8 hours (default)");
            await Assert.That(cache.Last).IsEqualTo((Tenant, ControlPlaneSettingsSnapshot.Empty));
        });
    }

    [Test]
    public async Task An_administrator_cannot_change_the_session_timeout()
    {
        await WithSqlite(async options =>
        {
            await using ZWardenDbContext ctx = Context(options);
            CapturingAuditWriter audit = new();
            ControlPlaneSettingsService service = Service(ctx, audit, new RecordingCache(), held: [Manage]); // no Tenant.Manage

            await Assert.That(async () => await service.SetSessionIdleTimeoutAsync(Actor, TimeSpan.FromMinutes(30)))
                .Throws<AuthorizationDeniedException>();
            await Assert.That(audit.Entries).IsEmpty();
        });
    }

    [Test]
    public async Task A_session_timeout_off_the_list_is_refused_and_settings_keep_the_other_values()
    {
        await WithSqlite(async options =>
        {
            await using ZWardenDbContext ctx = Context(options);
            ControlPlaneSettingsService service = Service(
                ctx, new CapturingAuditWriter(), new RecordingCache(), held: [Manage, Permissions.TenantManage.Name]);
            await service.SetInstanceNameAsync(Actor, "Knox Ops");
            await service.SetSessionIdleTimeoutAsync(Actor, TimeSpan.FromDays(7));

            await Assert.That(async () => await service.SetSessionIdleTimeoutAsync(Actor, TimeSpan.FromMinutes(5)))
                .Throws<ArgumentException>();
            await Assert.That(await service.GetAsync()).IsEqualTo(new ControlPlaneSettingsSnapshot("Knox Ops", TimeSpan.FromDays(7)));
        });
    }

    private static ZWardenDbContext Context(DbContextOptions options) => new(options, new TestTenantContext(Tenant));

    private static ControlPlaneSettingsService Service(
        ZWardenDbContext ctx, CapturingAuditWriter audit, RecordingCache cache, string[] held) =>
        new(ctx, new ControlPlaneSettingsRepository(ctx), new StubPermissionChecker(held), audit, cache, new TestTenantContext(Tenant));

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

    private sealed class RecordingCache : IControlPlaneSettingsCache
    {
        public (TenantId Tenant, ControlPlaneSettingsSnapshot Snapshot)? Last { get; private set; }

        public Task<ControlPlaneSettingsSnapshot> GetAsync(TenantId tenant, CancellationToken cancellationToken = default) =>
            Task.FromResult(ControlPlaneSettingsSnapshot.Empty);

        public void Remember(TenantId tenant, ControlPlaneSettingsSnapshot snapshot) => Last = (tenant, snapshot);
    }
}
