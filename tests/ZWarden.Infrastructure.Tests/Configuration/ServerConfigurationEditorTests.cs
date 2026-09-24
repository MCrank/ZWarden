using Microsoft.EntityFrameworkCore;
using ZWarden.Application.Configuration;
using ZWarden.Application.Operations;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Configuration;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Servers;
using ZWarden.Infrastructure.Tests.Agents;
using ZWarden.PzConfig.Revisions;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Configuration;

/// <summary>
/// F20b PR-3: the configuration-apply enqueue service, fail-closed (ADR 0018). It authorizes the server-scoped
/// <c>ServerConfigurationEdit</c> permission against the specific Server, resolves the Server through the tenant
/// filter (foreign/unknown ⇒ ServerNotFound), validates the edits, captures the last revision's hash as the
/// drift baseline (ADR 0011), audits, and enqueues a <b>mutating</b>, server-scoped Operation whose payload
/// carries the edits + baseline; a busy server (per-server lock, ADR 0022) surfaces as ServerBusy. Proven against
/// a real SQLite database, a real <see cref="PermissionChecker"/>, and seeded assignments.
/// </summary>
public class ServerConfigurationEditorTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyList<ConfigApplyEdit> Edits =
        [new ConfigApplyEdit("Zombies", ConfigEditKind.Number, "1")];

    [Test]
    public async Task Apply_enqueues_a_mutating_config_operation_carrying_the_edits_and_baseline()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerConfigurationEdit);
            await SeedRevisionAsync(options, serverId, PzConfigFile.SandboxVars, "base-hash-1");

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            CapturingAuditWriter audit = new();
            ServerConfigurationEditor sut = Editor(db, coordinator, audit);

            ServerConfigurationResult result = await sut.ApplyAsync(user, serverId, PzConfigFile.SandboxVars, Edits);

            await Assert.That(result.Succeeded).IsTrue();
            await Assert.That(result.Operation).IsNotNull();
            await Assert.That(coordinator.LastRequest!.Kind).IsEqualTo(OperationKind.ConfigApply);
            await Assert.That(coordinator.LastRequest!.IsMutating).IsTrue();
            await Assert.That(coordinator.LastRequest!.ServerId).IsEqualTo(serverId);
            await Assert.That(coordinator.LastRequest!.AgentId).IsEqualTo(agent);

            ConfigApplyPayload payload = ConfigApplyPayload.FromJson(coordinator.LastRequest!.CommandPayload!);
            await Assert.That(payload.File).IsEqualTo(PzConfigFile.SandboxVars);
            await Assert.That(payload.BaselineHash).IsEqualTo("base-hash-1");
            await Assert.That(payload.Edits.Count).IsEqualTo(1);
            await Assert.That(payload.Edits[0].Path).IsEqualTo("Zombies");
            await Assert.That(audit.Actions).Contains(ConfigurationAuditActions.Applied);
        });
    }

    [Test]
    public async Task Apply_uses_a_null_baseline_when_no_revision_has_been_recorded()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerConfigurationEdit);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerConfigurationEditor sut = Editor(db, coordinator, new CapturingAuditWriter());

            ServerConfigurationResult result = await sut.ApplyAsync(user, serverId, PzConfigFile.SandboxVars, Edits);

            await Assert.That(result.Succeeded).IsTrue();
            ConfigApplyPayload payload = ConfigApplyPayload.FromJson(coordinator.LastRequest!.CommandPayload!);
            await Assert.That(payload.BaselineHash).IsNull();
        });
    }

    [Test]
    public async Task Apply_uses_the_baseline_for_the_targeted_file_only()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerConfigurationEdit);
            // A revision for a *different* file must not become this file's baseline.
            await SeedRevisionAsync(options, serverId, PzConfigFile.Ini, "ini-hash");

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerConfigurationEditor sut = Editor(db, coordinator, new CapturingAuditWriter());

            await sut.ApplyAsync(user, serverId, PzConfigFile.SandboxVars, Edits);

            ConfigApplyPayload payload = ConfigApplyPayload.FromJson(coordinator.LastRequest!.CommandPayload!);
            await Assert.That(payload.BaselineHash).IsNull();
        });
    }

    [Test]
    public async Task Apply_uses_the_supplied_live_read_baseline_over_the_recorded_revision()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerConfigurationEdit);
            // A recorded revision exists, but the interactive editor drift-checks against what the operator
            // actually saw (the live read), not the last write — so the supplied baseline wins (F20c, ADR 0042).
            await SeedRevisionAsync(options, serverId, PzConfigFile.SandboxVars, "recorded-revision-hash");

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerConfigurationEditor sut = Editor(db, coordinator, new CapturingAuditWriter());

            ServerConfigurationResult result = await sut.ApplyAsync(
                user, serverId, PzConfigFile.SandboxVars, Edits, expectedBaselineHash: "live-read-hash");

            await Assert.That(result.Succeeded).IsTrue();
            ConfigApplyPayload payload = ConfigApplyPayload.FromJson(coordinator.LastRequest!.CommandPayload!);
            await Assert.That(payload.BaselineHash).IsEqualTo("live-read-hash");
        });
    }

    [Test]
    public async Task Apply_falls_back_to_the_recorded_revision_when_no_live_read_baseline_is_supplied()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerConfigurationEdit);
            await SeedRevisionAsync(options, serverId, PzConfigFile.SandboxVars, "recorded-revision-hash");

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerConfigurationEditor sut = Editor(db, coordinator, new CapturingAuditWriter());

            // A non-interactive caller (e.g. the mod manager) supplies no baseline — the recorded revision stands.
            await sut.ApplyAsync(user, serverId, PzConfigFile.SandboxVars, Edits);

            ConfigApplyPayload payload = ConfigApplyPayload.FromJson(coordinator.LastRequest!.CommandPayload!);
            await Assert.That(payload.BaselineHash).IsEqualTo("recorded-revision-hash");
        });
    }

    [Test]
    public async Task Apply_denies_without_the_server_scoped_config_edit_permission()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            // No ServerConfigurationEdit assignment.

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerConfigurationEditor sut = Editor(db, coordinator, new CapturingAuditWriter());

            ServerConfigurationResult result = await sut.ApplyAsync(user, serverId, PzConfigFile.SandboxVars, Edits);

            await Assert.That(result.Failure).IsEqualTo(ServerConfigurationFailure.NotAuthorized);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    public async Task Apply_reports_server_not_found_for_an_unknown_server()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId unknown = ServerId.New();
            await SeedAssignmentAsync(options, user, unknown, Permissions.ServerConfigurationEdit);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerConfigurationEditor sut = Editor(db, new RecordingCoordinator(), new CapturingAuditWriter());

            ServerConfigurationResult result = await sut.ApplyAsync(user, unknown, PzConfigFile.SandboxVars, Edits);

            await Assert.That(result.Failure).IsEqualTo(ServerConfigurationFailure.ServerNotFound);
        });
    }

    [Test]
    public async Task Apply_reports_server_busy_when_the_per_server_lock_refuses()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerConfigurationEdit);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerConfigurationEditor sut = Editor(db, new RecordingCoordinator { ThrowBusy = true }, new CapturingAuditWriter());

            ServerConfigurationResult result = await sut.ApplyAsync(user, serverId, PzConfigFile.SandboxVars, Edits);

            await Assert.That(result.Failure).IsEqualTo(ServerConfigurationFailure.ServerBusy);
        });
    }

    [Test]
    public async Task Apply_denies_an_empty_edit_set()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerConfigurationEdit);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerConfigurationEditor sut = Editor(db, coordinator, new CapturingAuditWriter());

            ServerConfigurationResult result = await sut.ApplyAsync(user, serverId, PzConfigFile.SandboxVars, []);

            await Assert.That(result.Failure).IsEqualTo(ServerConfigurationFailure.InvalidInput);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    public async Task Apply_denies_an_edit_with_an_empty_path()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerConfigurationEdit);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerConfigurationEditor sut = Editor(db, coordinator, new CapturingAuditWriter());

            ServerConfigurationResult result = await sut.ApplyAsync(
                user, serverId, PzConfigFile.SandboxVars, [new ConfigApplyEdit("  ", ConfigEditKind.Number, "1")]);

            await Assert.That(result.Failure).IsEqualTo(ServerConfigurationFailure.InvalidInput);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    public async Task Apply_rejects_an_edit_that_breaks_the_schema_and_enqueues_nothing()
    {
        // #223: an empty value for a schema boolean ("PVP=") is refused before it can reach the file, with a
        // message naming the key — even alongside a valid edit (the whole batch is refused).
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerConfigurationEdit);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerConfigurationEditor sut = Editor(db, coordinator, new CapturingAuditWriter());

            ServerConfigurationResult result = await sut.ApplyAsync(
                user, serverId, PzConfigFile.Ini,
                [
                    new ConfigApplyEdit("MaxPlayers", ConfigEditKind.Number, "16"),
                    new ConfigApplyEdit("PVP", ConfigEditKind.Text, ""),
                ]);

            await Assert.That(result.Failure).IsEqualTo(ServerConfigurationFailure.InvalidInput);
            await Assert.That(result.Message!).Contains("PVP");
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    [Arguments("DefaultPort", "16300")]
    [Arguments("UDPPort", "16301")]
    [Arguments("RCONPort", "27016")]
    public async Task Apply_refuses_an_edit_to_a_zwarden_managed_port_and_enqueues_nothing(string path, string value)
    {
        // #228: the container publishes PZ's fixed ports and the Agent dials RCON at 27015; the whole batch is refused.
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerConfigurationEdit);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerConfigurationEditor sut = Editor(db, coordinator, new CapturingAuditWriter());

            ServerConfigurationResult result = await sut.ApplyAsync(
                user, serverId, PzConfigFile.Ini,
                [
                    new ConfigApplyEdit("MaxPlayers", ConfigEditKind.Number, "16"),
                    new ConfigApplyEdit(path, ConfigEditKind.Number, value),
                ]);

            await Assert.That(result.Failure).IsEqualTo(ServerConfigurationFailure.InvalidInput);
            await Assert.That(result.Message!).Contains(path);
            await Assert.That(result.Message!).Contains("managed by ZWarden");
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    public async Task Restore_leaves_zwarden_managed_ports_as_they_are_and_restores_the_rest()
    {
        // #228: an old revision may hold a different port; restoring it would break the container mapping, so the
        // port is skipped and every other differing value is still restored.
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerConfigurationEdit);
            ConfigurationRevisionId target = await SeedSnapshotRevisionAsync(
                options, serverId, PzConfigFile.Ini, "[[\"DefaultPort\",\"s:16300\"],[\"MaxPlayers\",\"s:8\"]]", Now);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerConfigurationEditor sut = Editor(
                db, coordinator, new CapturingAuditWriter(),
                reader: new FixedReader(LiveView("live", ("DefaultPort", "16261"), ("MaxPlayers", "16"))));

            ServerConfigurationResult result = await sut.RestoreAsync(user, serverId, target);

            await Assert.That(result.Succeeded).IsTrue();
            ConfigApplyPayload payload = ConfigApplyPayload.FromJson(coordinator.LastRequest!.CommandPayload!);
            await Assert.That(payload.Edits.Count).IsEqualTo(1);
            await Assert.That(payload.Edits[0].Path).IsEqualTo("MaxPlayers");
        });
    }

    [Test]
    public async Task Restore_says_so_when_a_revision_differs_only_by_a_managed_port()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerConfigurationEdit);
            ConfigurationRevisionId target = await SeedSnapshotRevisionAsync(
                options, serverId, PzConfigFile.Ini, "[[\"RCONPort\",\"s:27016\"]]", Now);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerConfigurationEditor sut = Editor(
                db, coordinator, new CapturingAuditWriter(), reader: new FixedReader(LiveView("live", ("RCONPort", "27015"))));

            ServerConfigurationResult result = await sut.RestoreAsync(user, serverId, target);

            await Assert.That(result.Failure).IsEqualTo(ServerConfigurationFailure.InvalidInput);
            await Assert.That(result.Message!).Contains("managed by ZWarden");
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    public async Task Apply_passes_an_unknown_key_through_unvalidated()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerConfigurationEdit);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerConfigurationEditor sut = Editor(db, coordinator, new CapturingAuditWriter());

            ServerConfigurationResult result = await sut.ApplyAsync(
                user, serverId, PzConfigFile.Ini, [new ConfigApplyEdit("SomeModKey", ConfigEditKind.Text, "")]);

            await Assert.That(result.Succeeded).IsTrue();
        });
    }

    [Test]
    public async Task ApplyRaw_stages_the_text_then_enqueues_a_mutating_raw_operation()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerConfigurationEdit);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            CapturingAuditWriter audit = new();
            RecordingRawEditChannel channel = new();
            ServerConfigurationEditor sut = Editor(db, coordinator, audit, channel);

            ServerConfigurationResult result = await sut.ApplyRawAsync(
                user, serverId, PzConfigFile.SandboxVars, "SandboxVars = {\n    Zombies = 1,\n}\n",
                expectedBaselineHash: "live-read-hash");

            await Assert.That(result.Succeeded).IsTrue();
            // The whole-file text was staged to the owning Agent (not on the command payload).
            await Assert.That(channel.LastRawText).Contains("Zombies = 1");
            await Assert.That(coordinator.LastRequest!.Kind).IsEqualTo(OperationKind.ConfigApplyRaw);
            await Assert.That(coordinator.LastRequest!.IsMutating).IsTrue();
            await Assert.That(coordinator.LastRequest!.AgentId).IsEqualTo(agent);
            ConfigApplyRawPayload payload = ConfigApplyRawPayload.FromJson(coordinator.LastRequest!.CommandPayload!);
            await Assert.That(payload.File).IsEqualTo(PzConfigFile.SandboxVars);
            await Assert.That(payload.BaselineHash).IsEqualTo("live-read-hash");
            await Assert.That(payload.CorrelationId).IsEqualTo("corr-raw-1");
            await Assert.That(audit.Actions).Contains(ConfigurationAuditActions.RawApplied);
        });
    }

    [Test]
    public async Task ApplyRaw_reports_agent_offline_and_enqueues_nothing_when_staging_cannot_reach_the_host()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerConfigurationEdit);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerConfigurationEditor sut = Editor(db, coordinator, new CapturingAuditWriter(), new RecordingRawEditChannel { Offline = true });

            ServerConfigurationResult result = await sut.ApplyRawAsync(
                user, serverId, PzConfigFile.Ini, "PublicName=New\n", expectedBaselineHash: "h");

            await Assert.That(result.Failure).IsEqualTo(ServerConfigurationFailure.AgentOffline);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    public async Task ApplyRaw_denies_without_the_server_scoped_config_edit_permission()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerConfigurationEditor sut = Editor(db, coordinator, new CapturingAuditWriter());

            ServerConfigurationResult result = await sut.ApplyRawAsync(
                user, serverId, PzConfigFile.Ini, "PublicName=New\n");

            await Assert.That(result.Failure).IsEqualTo(ServerConfigurationFailure.NotAuthorized);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    public async Task Restore_enqueues_the_edits_that_move_current_to_the_target_revision()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerConfigurationEdit);
            // An older revision (Zombies = 4) is the restore target; the current state is Zombies = 1.
            ConfigurationRevisionId target = await SeedSnapshotRevisionAsync(
                options, serverId, PzConfigFile.SandboxVars, "[[\"Zombies\",\"n:4:i\"]]", Now);
            await SeedSnapshotRevisionAsync(
                options, serverId, PzConfigFile.SandboxVars, "[[\"Zombies\",\"n:1:i\"]]", Now.AddMinutes(5));
            string currentHash = PzValueSnapshot.Parse("[[\"Zombies\",\"n:1:i\"]]").Hash;

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            CapturingAuditWriter audit = new();
            ServerConfigurationEditor sut = Editor(db, coordinator, audit);

            ServerConfigurationResult result = await sut.RestoreAsync(user, serverId, target);

            await Assert.That(result.Succeeded).IsTrue();
            await Assert.That(coordinator.LastRequest!.Kind).IsEqualTo(OperationKind.ConfigApply);
            await Assert.That(coordinator.LastRequest!.IsMutating).IsTrue();
            ConfigApplyPayload payload = ConfigApplyPayload.FromJson(coordinator.LastRequest!.CommandPayload!);
            await Assert.That(payload.File).IsEqualTo(PzConfigFile.SandboxVars);
            await Assert.That(payload.BaselineHash).IsEqualTo(currentHash);
            await Assert.That(payload.Edits.Count).IsEqualTo(1);
            await Assert.That(payload.Edits[0].Path).IsEqualTo("Zombies");
            await Assert.That(payload.Edits[0].Kind).IsEqualTo(ConfigEditKind.Number);
            await Assert.That(payload.Edits[0].Value).IsEqualTo("4");
            await Assert.That(audit.Actions).Contains(ConfigurationAuditActions.Restored);
        });
    }

    [Test]
    public async Task Restore_plans_against_the_live_file_and_carries_its_baseline()
    {
        // #226: the file changed after ZWarden's last revision (Zombies 1 → 2 on disk). Planning against the stale
        // revision would be refused as drift; the restore plans against the live values and their baseline instead.
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerConfigurationEdit);
            ConfigurationRevisionId target = await SeedSnapshotRevisionAsync(
                options, serverId, PzConfigFile.SandboxVars, "[[\"Distribution\",\"n:1:i\"],[\"Zombies\",\"n:4:i\"]]", Now);
            await SeedSnapshotRevisionAsync(
                options, serverId, PzConfigFile.SandboxVars, "[[\"Distribution\",\"n:1:i\"],[\"Zombies\",\"n:1:i\"]]", Now.AddMinutes(5));

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerConfigurationEditor sut = Editor(
                db, coordinator, new CapturingAuditWriter(),
                reader: new FixedReader(LiveView("live-hash", ("Distribution", "1"), ("Zombies", "2"))));

            ServerConfigurationResult result = await sut.RestoreAsync(user, serverId, target);

            await Assert.That(result.Succeeded).IsTrue();
            ConfigApplyPayload payload = ConfigApplyPayload.FromJson(coordinator.LastRequest!.CommandPayload!);
            await Assert.That(payload.BaselineHash).IsEqualTo("live-hash");
            await Assert.That(payload.Edits.Count).IsEqualTo(1);
            await Assert.That(payload.Edits[0].Path).IsEqualTo("Zombies");
            await Assert.That(payload.Edits[0].Value).IsEqualTo("4");
        });
    }

    [Test]
    public async Task Restore_reports_already_matching_when_the_live_file_holds_the_target_values()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerConfigurationEdit);
            ConfigurationRevisionId target = await SeedSnapshotRevisionAsync(
                options, serverId, PzConfigFile.SandboxVars, "[[\"Zombies\",\"n:4:i\"]]", Now);
            await SeedSnapshotRevisionAsync(
                options, serverId, PzConfigFile.SandboxVars, "[[\"Zombies\",\"n:1:i\"]]", Now.AddMinutes(5));

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerConfigurationEditor sut = Editor(
                db, coordinator, new CapturingAuditWriter(), reader: new FixedReader(LiveView("h", ("Zombies", "4"))));

            ServerConfigurationResult result = await sut.RestoreAsync(user, serverId, target);

            await Assert.That(result.Failure).IsEqualTo(ServerConfigurationFailure.InvalidInput);
            await Assert.That(result.Message!).Contains("already matches");
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    public async Task Restore_denies_when_the_revision_already_matches_the_current_state()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerConfigurationEdit);
            // The only revision is also the current state: restoring it is a no-op.
            ConfigurationRevisionId only = await SeedSnapshotRevisionAsync(
                options, serverId, PzConfigFile.SandboxVars, "[[\"Zombies\",\"n:4:i\"]]", Now);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerConfigurationEditor sut = Editor(db, coordinator, new CapturingAuditWriter());

            ServerConfigurationResult result = await sut.RestoreAsync(user, serverId, only);

            await Assert.That(result.Failure).IsEqualTo(ServerConfigurationFailure.InvalidInput);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    public async Task Restore_reports_server_not_found_for_a_revision_of_another_server()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            ServerId other = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerConfigurationEdit);
            ConfigurationRevisionId foreign = await SeedSnapshotRevisionAsync(
                options, other, PzConfigFile.SandboxVars, "[[\"Zombies\",\"n:4:i\"]]", Now);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerConfigurationEditor sut = Editor(db, coordinator, new CapturingAuditWriter());

            ServerConfigurationResult result = await sut.RestoreAsync(user, serverId, foreign);

            await Assert.That(result.Failure).IsEqualTo(ServerConfigurationFailure.ServerNotFound);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    public async Task Restore_denies_without_the_server_scoped_config_edit_permission()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            ConfigurationRevisionId target = await SeedSnapshotRevisionAsync(
                options, serverId, PzConfigFile.SandboxVars, "[[\"Zombies\",\"n:4:i\"]]", Now);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerConfigurationEditor sut = Editor(db, coordinator, new CapturingAuditWriter());

            ServerConfigurationResult result = await sut.RestoreAsync(user, serverId, target);

            await Assert.That(result.Failure).IsEqualTo(ServerConfigurationFailure.NotAuthorized);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    public async Task Restore_reports_server_busy_when_the_per_server_lock_refuses()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerConfigurationEdit);
            ConfigurationRevisionId target = await SeedSnapshotRevisionAsync(
                options, serverId, PzConfigFile.SandboxVars, "[[\"Zombies\",\"n:4:i\"]]", Now);
            await SeedSnapshotRevisionAsync(
                options, serverId, PzConfigFile.SandboxVars, "[[\"Zombies\",\"n:1:i\"]]", Now.AddMinutes(5));

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerConfigurationEditor sut = Editor(db, new RecordingCoordinator { ThrowBusy = true }, new CapturingAuditWriter());

            ServerConfigurationResult result = await sut.RestoreAsync(user, serverId, target);

            await Assert.That(result.Failure).IsEqualTo(ServerConfigurationFailure.ServerBusy);
        });
    }

    private static async Task<ConfigurationRevisionId> SeedSnapshotRevisionAsync(
        DbContextOptions options, ServerId server, PzConfigFile file, string canonicalText, DateTimeOffset at)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
        PzValueSnapshot snapshot = PzValueSnapshot.Parse(canonicalText);
        ConfigurationRevision revision = ConfigurationRevision.Record(server, file, snapshot.CanonicalText, snapshot.Hash, at);
        new ConfigurationRevisionRepository(db).Add(revision);
        await db.SaveChangesAsync();
        return revision.Id;
    }

    private static ServerConfigurationEditor Editor(
        ZWardenDbContext db, RecordingCoordinator coordinator, CapturingAuditWriter audit, RecordingRawEditChannel? rawChannel = null,
        IServerConfigurationReader? reader = null)
        => new(
            new ServerRepository(db),
            new PermissionChecker(db, new TestTenantContext(Tenant)),
            coordinator,
            new ConfigurationRevisionRepository(db),
            rawChannel ?? new RecordingRawEditChannel(),
            reader ?? new FixedReader(ConfigDocumentView.OfOutcome(ConfigReadOutcome.AgentOffline)),
            audit);

    // A reader returning a fixed view (#226); the default is an offline host, so restore falls back to the recorded
    // revision exactly as before.
    private sealed class FixedReader(ConfigDocumentView view) : IServerConfigurationReader
    {
        public Task<ConfigDocumentView> ReadAsync(
            UserId user, ServerId server, PzConfigFile file, CancellationToken cancellationToken = default) =>
            Task.FromResult(view);
    }

    private static ConfigDocumentView LiveView(string baseline, params (string Path, string Value)[] settings) => new(
        ConfigReadOutcome.Read,
        [
            new ConfigSection("Other",
            [
                .. settings.Select(s => new ConfigSettingView(
                    s.Path, s.Path, ConfigEditKind.Number, ConfigValueShape.Whole, s.Value, null, null, null, null, [], false)),
            ]),
        ],
        "raw",
        baseline,
        [],
        null);

    private sealed class RecordingRawEditChannel : IServerConfigRawEditChannel
    {
        public bool Offline { get; init; }

        public string? LastRawText { get; private set; }

        public Task<ConfigRawEditStage> StageAsync(
            ServerId server, AgentId owningAgent, string rawText, CancellationToken cancellationToken = default)
        {
            LastRawText = rawText;
            return Task.FromResult(Offline ? ConfigRawEditStage.Offline() : ConfigRawEditStage.Ok("corr-raw-1"));
        }
    }

    private sealed class RecordingCoordinator : IOperationCoordinator
    {
        public bool ThrowBusy { get; init; }

        public EnqueueOperationRequest? LastRequest { get; private set; }

        public Task<Operation> EnqueueAsync(
            EnqueueOperationRequest request, UserId? actor = null, CancellationToken cancellationToken = default)
        {
            if (ThrowBusy)
            {
                throw new ServerBusyException(request.ServerId!.Value);
            }

            LastRequest = request;
            return Task.FromResult(Operation.Enqueue(
                request.AgentId, request.Kind, request.IsMutating, request.IdempotencyKey, Now,
                request.ServerId, request.CommandPayload));
        }

        public Task<Operation> RequestCancellationAsync(
            OperationId operationId, UserId? actor = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private static async Task<ServerId> SeedServerAsync(DbContextOptions options, AgentId agent)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
        Server server = Server.Import(agent, ServerId.New(), "survivors", Now);
        new ServerRepository(db).Add(server);
        await db.SaveChangesAsync();
        return server.Id;
    }

    private static async Task SeedRevisionAsync(DbContextOptions options, ServerId server, PzConfigFile file, string hash)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
        new ConfigurationRevisionRepository(db).Add(
            ConfigurationRevision.Record(server, file, "[[\"Zombies\",\"n:4:i\"]]", hash, Now));
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
