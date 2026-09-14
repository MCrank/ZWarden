using Microsoft.EntityFrameworkCore;
using ZWarden.Application.Configuration;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Configuration;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Servers;
using ZWarden.PzConfig.Revisions;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Configuration;

/// <summary>
/// F20b PR-4: the configuration-history read side. It returns a Server's Configuration Revisions for one file,
/// newest first, each carrying its value-level diff from the preceding revision (ADR 0011 — parsed values, read
/// back from the persisted canonical snapshot; the control plane holds no live file). Fail-closed (ADR 0018): an
/// unknown Server or a caller without <c>ServerConfigurationEdit</c> sees an empty history. Proven against a real
/// SQLite database and a real <see cref="PermissionChecker"/>.
/// </summary>
public class ConfigurationHistoryServiceTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task History_lists_revisions_newest_first_with_the_diff_from_the_predecessor()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerConfigurationEdit);
            await SeedRevisionAsync(options, serverId, PzConfigFile.SandboxVars, "[[\"Zombies\",\"n:4:i\"]]", Now);
            await SeedRevisionAsync(options, serverId, PzConfigFile.SandboxVars, "[[\"Zombies\",\"n:1:i\"]]", Now.AddMinutes(5));
            await SeedRevisionAsync(options, serverId, PzConfigFile.SandboxVars, "[[\"Speed\",\"n:2:i\"],[\"Zombies\",\"n:1:i\"]]", Now.AddMinutes(10));

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ConfigurationHistoryService sut = Service(db);

            IReadOnlyList<ConfigRevisionView> history = await sut.GetHistoryAsync(user, serverId, PzConfigFile.SandboxVars);

            await Assert.That(history.Count).IsEqualTo(3);
            // Newest first; only the newest is current.
            await Assert.That(history[0].IsCurrent).IsTrue();
            await Assert.That(history[1].IsCurrent).IsFalse();
            await Assert.That(history[0].RecordedAt).IsGreaterThan(history[1].RecordedAt);
            await Assert.That(history[0].ShortHash.Length).IsEqualTo(12);

            // Newest introduced Speed relative to the middle revision.
            await Assert.That(history[0].Changes.Count).IsEqualTo(1);
            await Assert.That(history[0].Changes[0].Path).IsEqualTo("Speed");
            await Assert.That(history[0].Changes[0].Kind).IsEqualTo(ConfigChangeKind.Added);
            await Assert.That(history[0].Changes[0].After).IsEqualTo("2");

            // Middle changed Zombies from 4 to 1.
            await Assert.That(history[1].Changes[0].Path).IsEqualTo("Zombies");
            await Assert.That(history[1].Changes[0].Kind).IsEqualTo(ConfigChangeKind.Changed);
            await Assert.That(history[1].Changes[0].Before).IsEqualTo("4");
            await Assert.That(history[1].Changes[0].After).IsEqualTo("1");

            // Earliest is the baseline: nothing before it, so no diff.
            await Assert.That(history[2].Changes).IsEmpty();
        });
    }

    [Test]
    public async Task History_scopes_to_the_requested_file()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerConfigurationEdit);
            await SeedRevisionAsync(options, serverId, PzConfigFile.SandboxVars, "[[\"Zombies\",\"n:4:i\"]]", Now);
            await SeedRevisionAsync(options, serverId, PzConfigFile.Ini, "[[\"PublicName\",\"s:My Server\"]]", Now);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ConfigurationHistoryService sut = Service(db);

            IReadOnlyList<ConfigRevisionView> history = await sut.GetHistoryAsync(user, serverId, PzConfigFile.Ini);

            await Assert.That(history.Count).IsEqualTo(1);
            await Assert.That(history[0].File).IsEqualTo(PzConfigFile.Ini);
        });
    }

    [Test]
    public async Task History_is_empty_without_the_config_edit_permission()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options);
            await SeedRevisionAsync(options, serverId, PzConfigFile.SandboxVars, "[[\"Zombies\",\"n:4:i\"]]", Now);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ConfigurationHistoryService sut = Service(db);

            IReadOnlyList<ConfigRevisionView> history = await sut.GetHistoryAsync(user, serverId, PzConfigFile.SandboxVars);

            await Assert.That(history).IsEmpty();
        });
    }

    [Test]
    public async Task History_is_empty_for_an_unknown_server()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId unknown = ServerId.New();
            await SeedAssignmentAsync(options, user, unknown, Permissions.ServerConfigurationEdit);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ConfigurationHistoryService sut = Service(db);

            IReadOnlyList<ConfigRevisionView> history = await sut.GetHistoryAsync(user, unknown, PzConfigFile.SandboxVars);

            await Assert.That(history).IsEmpty();
        });
    }

    private static ConfigurationHistoryService Service(ZWardenDbContext db) => new(
        new ServerRepository(db),
        new PermissionChecker(db, new TestTenantContext(Tenant)),
        new ConfigurationRevisionRepository(db));

    private static async Task<ServerId> SeedServerAsync(DbContextOptions options)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
        Server server = Server.Import(AgentId.New(), ServerId.New(), "history", Now);
        new ServerRepository(db).Add(server);
        await db.SaveChangesAsync();
        return server.Id;
    }

    private static async Task SeedRevisionAsync(
        DbContextOptions options, ServerId server, PzConfigFile file, string canonicalText, DateTimeOffset at)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
        PzValueSnapshot snapshot = PzValueSnapshot.Parse(canonicalText);
        new ConfigurationRevisionRepository(db).Add(
            ConfigurationRevision.Record(server, file, snapshot.CanonicalText, snapshot.Hash, at));
        await db.SaveChangesAsync();
    }

    private static async Task SeedAssignmentAsync(
        DbContextOptions options, UserId user, ServerId server, PermissionDefinition permission)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
        Role role = Role.CreateCustom(Tenant, $"role-{Guid.NewGuid():N}");
        role.Grant(permission);
        db.Set<Role>().Add(role);
        db.Set<RoleAssignment>().Add(RoleAssignment.ForServer(Tenant, user, role.Id, server));
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
}
