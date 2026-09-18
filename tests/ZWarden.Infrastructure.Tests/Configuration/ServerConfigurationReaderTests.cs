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
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Configuration;

/// <summary>
/// F20c (ADR 0041): the tenant-scoped Configuration View reader, fail-closed (ADR 0018). It resolves the Server
/// through the tenant filter (foreign/unknown ⇒ ServerNotFound), authorizes the server-scoped
/// <c>ServerConfigurationEdit</c> permission before touching the read channel, routes the read to the Server's
/// owning Agent, and on a successful read overlays the ZWarden schema (label, section, shape, range, default) and
/// turns each harvested comment into a Setting Tooltip — a key with no schema entry landing in "Other", unvalidated.
/// The channel's offline/timeout/missing/parse-failed statuses each surface as a first-class outcome. Proven against
/// a real SQLite database, a real <see cref="PermissionChecker"/>, and a captured fake read channel.
/// </summary>
public class ServerConfigurationReaderTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Read_overlays_schema_metadata_and_comment_tooltips_and_routes_to_the_owning_agent()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerConfigurationEdit);

            FakeReadChannel channel = new(new ConfigReadTransfer(
                ConfigTransferStatus.Read,
                [
                    new ConfigTransferSetting(
                        "Zombies", ConfigEditKind.Number, "2",
                        "How fast the zombies move.\n1 = Sprinters\n2 = Fast Shamblers"),
                    new ConfigTransferSetting("SomeMod.Custom", ConfigEditKind.Text, "hi", "A mod-added key."),
                ],
                "SandboxVars = {}\n",
                "base-hash-xyz",
                []));

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerConfigurationReader sut = Reader(db, channel);

            ConfigDocumentView view = await sut.ReadAsync(user, serverId, PzConfigFile.SandboxVars);

            await Assert.That(view.Outcome).IsEqualTo(ConfigReadOutcome.Read);
            await Assert.That(view.RawText).IsEqualTo("SandboxVars = {}\n");
            await Assert.That(view.BaselineHash).IsEqualTo("base-hash-xyz");

            // Routed to the Server's owning Agent.
            await Assert.That(channel.LastAgent).IsEqualTo(agent);
            await Assert.That(channel.LastFile).IsEqualTo(PzConfigFile.SandboxVars);

            // The known key carries schema label/section/range/default and a comment-sourced tooltip + options.
            ConfigSettingView zombies = view.Sections
                .SelectMany(s => s.Settings).Single(s => s.Path == "Zombies");
            await Assert.That(zombies.KnownToSchema).IsTrue();
            await Assert.That(zombies.Label).IsEqualTo("Population");
            await Assert.That(zombies.Shape).IsEqualTo(ConfigValueShape.Whole);
            await Assert.That(zombies.Min).IsEqualTo(1d);
            await Assert.That(zombies.Max).IsEqualTo(6d);
            await Assert.That(zombies.Default).IsEqualTo("4");
            await Assert.That(zombies.Tooltip!).Contains("How fast the zombies move.");
            await Assert.That(zombies.Options.Count).IsEqualTo(2);
            await Assert.That(zombies.Options[1].Label).IsEqualTo("Fast Shamblers");

            ConfigSection zombieSection = view.Sections.Single(s => s.Settings.Any(x => x.Path == "Zombies"));
            await Assert.That(zombieSection.Name).IsEqualTo("Zombies");
        });
    }

    [Test]
    public async Task Read_puts_an_unknown_key_in_other_unvalidated_with_a_comment_only_tooltip()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerConfigurationEdit);

            FakeReadChannel channel = new(new ConfigReadTransfer(
                ConfigTransferStatus.Read,
                [
                    new ConfigTransferSetting("Zombies", ConfigEditKind.Number, "2", null),
                    new ConfigTransferSetting("SomeMod.Custom", ConfigEditKind.Text, "hi", "A mod-added key."),
                ],
                "x", "h", []));

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ConfigDocumentView view = await Reader(db, channel).ReadAsync(user, serverId, PzConfigFile.SandboxVars);

            ConfigSection other = view.Sections.Single(s => s.Name == "Other");
            ConfigSettingView unknown = other.Settings.Single(s => s.Path == "SomeMod.Custom");
            await Assert.That(unknown.KnownToSchema).IsFalse();
            await Assert.That(unknown.Min).IsNull();
            await Assert.That(unknown.Tooltip).IsEqualTo("A mod-added key.");

            // "Other" always sorts last.
            await Assert.That(view.Sections[^1].Name).IsEqualTo("Other");
        });
    }

    [Test]
    public async Task Read_denies_without_the_server_scoped_config_edit_permission_and_never_reads()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            // No ServerConfigurationEdit assignment.

            FakeReadChannel channel = new(ConfigReadTransfer.OfStatus(ConfigTransferStatus.Read));
            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));

            ConfigDocumentView view = await Reader(db, channel).ReadAsync(user, serverId, PzConfigFile.SandboxVars);

            await Assert.That(view.Outcome).IsEqualTo(ConfigReadOutcome.NotAuthorized);
            await Assert.That(channel.Called).IsFalse();
        });
    }

    [Test]
    public async Task Read_reports_server_not_found_for_an_unknown_server()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId unknown = ServerId.New();
            await SeedAssignmentAsync(options, user, unknown, Permissions.ServerConfigurationEdit);

            FakeReadChannel channel = new(ConfigReadTransfer.OfStatus(ConfigTransferStatus.Read));
            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));

            ConfigDocumentView view = await Reader(db, channel).ReadAsync(user, unknown, PzConfigFile.SandboxVars);

            await Assert.That(view.Outcome).IsEqualTo(ConfigReadOutcome.ServerNotFound);
            await Assert.That(channel.Called).IsFalse();
        });
    }

    [Test]
    public async Task Read_passes_through_the_transport_unavailability_outcomes()
    {
        foreach ((ConfigTransferStatus status, ConfigReadOutcome expected) in new[]
        {
            (ConfigTransferStatus.AgentOffline, ConfigReadOutcome.AgentOffline),
            (ConfigTransferStatus.TimedOut, ConfigReadOutcome.TimedOut),
            (ConfigTransferStatus.FileMissing, ConfigReadOutcome.FileMissing),
        })
        {
            await WithSqlite(async options =>
            {
                UserId user = UserId.New();
                ServerId serverId = await SeedServerAsync(options, AgentId.New());
                await SeedAssignmentAsync(options, user, serverId, Permissions.ServerConfigurationEdit);

                FakeReadChannel channel = new(ConfigReadTransfer.OfStatus(status));
                await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));

                ConfigDocumentView view = await Reader(db, channel).ReadAsync(user, serverId, PzConfigFile.SandboxVars);

                await Assert.That(view.Outcome).IsEqualTo(expected);
                await Assert.That(view.Sections).IsEmpty();
            });
        }
    }

    [Test]
    public async Task Read_surfaces_a_parse_failure_with_the_raw_text_and_diagnostics()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerConfigurationEdit);

            FakeReadChannel channel = new(new ConfigReadTransfer(
                ConfigTransferStatus.ParseFailed,
                [],
                "SandboxVars = {\n  Zombies = ,\n",
                null,
                [new ConfigTransferDiagnostic("unexpected token", 2, 13)]));

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ConfigDocumentView view = await Reader(db, channel).ReadAsync(user, serverId, PzConfigFile.SandboxVars);

            await Assert.That(view.Outcome).IsEqualTo(ConfigReadOutcome.ParseFailed);
            await Assert.That(view.Sections).IsEmpty();
            await Assert.That(view.RawText).Contains("Zombies = ,");
            await Assert.That(view.Diagnostics.Count).IsEqualTo(1);
            await Assert.That(view.Diagnostics[0].Line).IsEqualTo(2);
        });
    }

    private static ServerConfigurationReader Reader(ZWardenDbContext db, IServerConfigReadChannel channel) =>
        new(new ServerRepository(db), new PermissionChecker(db, new TestTenantContext(Tenant)), channel);

    private sealed class FakeReadChannel(ConfigReadTransfer transfer) : IServerConfigReadChannel
    {
        public bool Called { get; private set; }

        public AgentId? LastAgent { get; private set; }

        public PzConfigFile? LastFile { get; private set; }

        public Task<ConfigReadTransfer> ReadAsync(
            ServerId server, AgentId owningAgent, PzConfigFile file, CancellationToken cancellationToken = default)
        {
            Called = true;
            LastAgent = owningAgent;
            LastFile = file;
            return Task.FromResult(transfer);
        }
    }

    private static async Task<ServerId> SeedServerAsync(DbContextOptions options, AgentId agent)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
        Server server = Server.Import(agent, ServerId.New(), "survivors", Now);
        new ServerRepository(db).Add(server);
        await db.SaveChangesAsync();
        return server.Id;
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
