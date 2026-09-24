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
using ZWarden.PzConfig;
using ZWarden.PzConfig.Model;
using ZWarden.PzConfig.Revisions;
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
    public async Task Read_files_a_mod_table_in_its_own_section_and_an_unmatched_key_in_other_both_unvalidated()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerConfigurationEdit);

            FakeReadChannel channel = new(new ConfigReadTransfer(
                ConfigTransferStatus.Read,
                [
                    new ConfigTransferSetting("Zzyzx", ConfigEditKind.Text, "?", null),
                    new ConfigTransferSetting("SomeMod.CustomOption", ConfigEditKind.Text, "hi", "A mod-added key."),
                    new ConfigTransferSetting("Zombies", ConfigEditKind.Number, "2", null),
                ],
                "x", "h", []));

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ConfigDocumentView view = await Reader(db, channel).ReadAsync(user, serverId, PzConfigFile.SandboxVars);

            // #227: a mod's nested sandbox table becomes its own section, with a humanized label.
            ConfigSettingView mod = view.Sections.Single(s => s.Name == "Some mod").Settings.Single();
            await Assert.That(mod.Path).IsEqualTo("SomeMod.CustomOption");
            await Assert.That(mod.Label).IsEqualTo("Custom option");
            await Assert.That(mod.KnownToSchema).IsFalse();
            await Assert.That(mod.Min).IsNull();
            await Assert.That(mod.Tooltip).IsEqualTo("A mod-added key.");

            ConfigSettingView unknown = view.Sections.Single(s => s.Name == "Other").Settings.Single();
            await Assert.That(unknown.Path).IsEqualTo("Zzyzx");

            // Vanilla sections first, then mod sections, then "Other" — whatever the file order.
            await Assert.That(string.Join(" | ", view.Sections.Select(s => s.Name))).IsEqualTo("Zombies | Some mod | Other");
        });
    }

    [Test]
    public async Task Read_of_a_full_b42_file_classifies_every_vanilla_key_and_types_ini_values()
    {
        // #227: against every vanilla key of a B42 file (the live pass found 132 INI / 260 sandbox keys in "Other").
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerConfigurationEdit);
            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));

            ConfigDocumentView ini = await Reader(db, new FakeReadChannel(FixtureTransfer(PzConfigKind.Ini, "b42-servertest.ini")))
                .ReadAsync(user, serverId, PzConfigFile.Ini);
            ConfigDocumentView sandbox = await Reader(db, new FakeReadChannel(FixtureTransfer(PzConfigKind.SandboxVars, "b42-SandboxVars.lua")))
                .ReadAsync(user, serverId, PzConfigFile.SandboxVars);

            await Assert.That(ini.Sections.Any(s => s.Name == "Other")).IsFalse();
            await Assert.That(sandbox.Sections.Any(s => s.Name == "Other")).IsFalse();

            Dictionary<string, ConfigSettingView> iniByPath = ini.Sections.SelectMany(s => s.Settings).ToDictionary(s => s.Path);
            ConfigSettingView adminSafehouse = iniByPath["AdminSafehouse"];
            await Assert.That(adminSafehouse.Label).IsEqualTo("Admin safehouse");
            await Assert.That(adminSafehouse.Shape).IsEqualTo(ConfigValueShape.Boolean);
            await Assert.That(ini.Sections.Single(s => s.Settings.Contains(adminSafehouse)).Name).IsEqualTo("Safehouse");

            // An unschema'd number takes the game's range and default from its comment, which leaves the tooltip.
            ConfigSettingView timer = iniByPath["SafetyToggleTimer"];
            await Assert.That(timer.Shape).IsEqualTo(ConfigValueShape.Whole);
            await Assert.That(timer.Min).IsEqualTo(0d);
            await Assert.That(timer.Max).IsEqualTo(1000d);
            await Assert.That(timer.Default).IsEqualTo("2");
            await Assert.That(timer.Tooltip!).DoesNotContain("Min:");

            // #228: the ports are flagged ZWarden-managed so the editor renders them read-only; nothing else is.
            await Assert.That(iniByPath["DefaultPort"].Managed).IsTrue();
            await Assert.That(iniByPath["UDPPort"].Managed).IsTrue();
            await Assert.That(iniByPath["RCONPort"].Managed).IsTrue();
            await Assert.That(ini.Sections.SelectMany(s => s.Settings).Count(s => s.Managed)).IsEqualTo(3);

            // Sections follow the in-game order, with the mod's own table after every vanilla section.
            await Assert.That(ini.Sections[0].Name).IsEqualTo("Details");
            await Assert.That(sandbox.Sections[0].Name).IsEqualTo("Zombies");
            await Assert.That(sandbox.Sections[^1].Name).IsEqualTo("Better lockpicking");
        });
    }

    // Parses a fixture the way the Agent's config reader does (ServerConfigReader) and hands the reader its settings.
    private static ConfigReadTransfer FixtureTransfer(PzConfigKind kind, string fixture)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));
        PzConfigReadResult read = new PzConfigParser().Open(kind, bytes);
        List<ConfigTransferSetting> settings =
        [
            .. PzValueSnapshot.Of(read.Document!).Scalars.Select(s => new ConfigTransferSetting(
                s.Path,
                s.Value switch
                {
                    PzBoolean => ConfigEditKind.Bool,
                    PzNumber => ConfigEditKind.Number,
                    _ => ConfigEditKind.Text,
                },
                s.Value switch
                {
                    PzBoolean b => b.Value ? "true" : "false",
                    PzNumber n => n.Lexeme,
                    PzString str => str.Value,
                    _ => string.Empty,
                },
                read.Comments.TryGetValue(s.Path, out string? comment) ? comment : null)),
        ];
        return new ConfigReadTransfer(ConfigTransferStatus.Read, settings, "raw", "hash", []);
    }

    [Test]
    public async Task Read_types_ini_text_values_as_booleans_and_numbers_for_the_editor()
    {
        // #223/#227: the Agent's INI reader reports every value as Text (the INI has no types). The view must
        // re-type them — schema booleans and bare true/false as Bool toggles, integer/decimal strings as numbers —
        // so a toggle's posted value is interpreted as a boolean. Credential/free-text keys stay text boxes.
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerConfigurationEdit);

            FakeReadChannel channel = new(new ConfigReadTransfer(
                ConfigTransferStatus.Read,
                [
                    new ConfigTransferSetting("PVP", ConfigEditKind.Text, "true", null),
                    new ConfigTransferSetting("AdminSafehouse", ConfigEditKind.Text, "false", null),
                    new ConfigTransferSetting("MaxPlayers", ConfigEditKind.Text, "16", null),
                    new ConfigTransferSetting("PingLimit", ConfigEditKind.Text, "400", null),
                    new ConfigTransferSetting("SafehouseDaySurvivedToClaim", ConfigEditKind.Text, "0.5", null),
                    new ConfigTransferSetting("Password", ConfigEditKind.Text, "1234", null),
                    new ConfigTransferSetting("DiscordToken", ConfigEditKind.Text, "true", null),
                    new ConfigTransferSetting("Mods", ConfigEditKind.Text, "", null),
                ],
                "PVP=true\n", "h", []));

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ConfigDocumentView view = await Reader(db, channel).ReadAsync(user, serverId, PzConfigFile.Ini);
            Dictionary<string, ConfigSettingView> byPath = view.Sections
                .SelectMany(s => s.Settings).ToDictionary(s => s.Path, StringComparer.Ordinal);

            await AssertTyped(byPath["PVP"], ConfigEditKind.Bool, ConfigValueShape.Boolean);
            await AssertTyped(byPath["AdminSafehouse"], ConfigEditKind.Bool, ConfigValueShape.Boolean);
            await AssertTyped(byPath["MaxPlayers"], ConfigEditKind.Number, ConfigValueShape.Whole);
            await AssertTyped(byPath["PingLimit"], ConfigEditKind.Number, ConfigValueShape.Whole);
            await AssertTyped(byPath["SafehouseDaySurvivedToClaim"], ConfigEditKind.Number, ConfigValueShape.Fractional);
            await AssertTyped(byPath["Password"], ConfigEditKind.Text, ConfigValueShape.Text);
            await AssertTyped(byPath["DiscordToken"], ConfigEditKind.Text, ConfigValueShape.Text);
            await AssertTyped(byPath["Mods"], ConfigEditKind.Text, ConfigValueShape.Text);

            // A well-formed boolean renders as a plain toggle — no choice list.
            await Assert.That(byPath["PVP"].Options).IsEmpty();
        });
    }

    [Test]
    public async Task Read_offers_an_on_off_choice_for_a_schema_boolean_whose_value_is_not_a_boolean()
    {
        // #223 recovery: a file already damaged to "PVP=" must not render as an Off toggle — an unrelated apply
        // would then silently write PVP=false. It is offered as an On/Off choice that keeps the current (empty)
        // value selected, so it only changes when the operator picks one.
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerConfigurationEdit);

            FakeReadChannel channel = new(new ConfigReadTransfer(
                ConfigTransferStatus.Read,
                [new ConfigTransferSetting("PVP", ConfigEditKind.Text, "", null)],
                "PVP=\n", "h", []));

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ConfigDocumentView view = await Reader(db, channel).ReadAsync(user, serverId, PzConfigFile.Ini);
            ConfigSettingView pvp = view.Sections.SelectMany(s => s.Settings).Single();

            await Assert.That(pvp.Kind).IsEqualTo(ConfigEditKind.Bool);
            await Assert.That(pvp.Value).IsEqualTo(string.Empty);
            await Assert.That(string.Join(",", pvp.Options.Select(o => o.Value))).IsEqualTo("true,false");
        });
    }

    private static async Task AssertTyped(ConfigSettingView setting, ConfigEditKind kind, ConfigValueShape shape)
    {
        await Assert.That(setting.Kind).IsEqualTo(kind).Because(setting.Path);
        await Assert.That(setting.Shape).IsEqualTo(shape).Because(setting.Path);
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
