using Microsoft.EntityFrameworkCore;
using ZWarden.Application.Configuration;
using ZWarden.Application.Mods;
using ZWarden.Application.Operations;
using ZWarden.Application.Servers;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Mods;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Configuration;
using ZWarden.Infrastructure.Mods;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Servers;
using ZWarden.Infrastructure.Tests.Agents;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Mods;

/// <summary>
/// F22: the mod-management service, fail-closed (ADR 0018). Config-as-truth â enable/disable/reorder/add/remove
/// recompute <c>WorkshopItems=</c>/<c>Mods=</c> from the last observed Mod Inventory (F21) and enqueue an F20b
/// config-apply on the Server's Agent (mutating, per-server lock, drift baseline, audited under Mod.*); update
/// enqueues the F17 UpdateServer. Each verb authorizes its mapped server-scoped Mod.* permission, resolves the
/// Server through the tenant filter, and reads the inventory ownership-guarded. Proven against a real SQLite
/// database and a real <see cref="PermissionChecker"/>.
/// </summary>
public class ServerModManagerTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Enable_enqueues_a_config_apply_that_appends_to_the_mods_list()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ModInstall);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            CapturingAuditWriter audit = new();
            ModInventoryCache cache = new();
            cache.Record(Inventory(serverId, agent, workshop: ["100"], enabled: ["A"]));
            ServerModManager sut = Manager(db, coordinator, audit, cache);

            ModManagementResult result = await sut.EnableModsAsync(user, serverId, ["B"]);

            await Assert.That(result.Succeeded).IsTrue();
            await Assert.That(coordinator.LastRequest!.Kind).IsEqualTo(OperationKind.ConfigApply);
            await Assert.That(coordinator.LastRequest!.IsMutating).IsTrue();
            await Assert.That(coordinator.LastRequest!.ServerId).IsEqualTo(serverId);
            await Assert.That(coordinator.LastRequest!.AgentId).IsEqualTo(agent);

            ConfigApplyPayload payload = ConfigApplyPayload.FromJson(coordinator.LastRequest!.CommandPayload!);
            await Assert.That(payload.File).IsEqualTo(PzConfigFile.Ini);
            await Assert.That(payload.Edits.Count).IsEqualTo(1);
            await Assert.That(payload.Edits[0]).IsEqualTo(new ConfigApplyEdit("Mods", ConfigEditKind.Text, "A;B"));
            await Assert.That(audit.Actions).Contains(ModAuditActions.Enabled);
        });
    }

    [Test]
    public async Task Enable_denies_without_the_server_scoped_mod_install_permission()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            // A Mod.Remove grant does not authorize an enable (Mod.Install).
            await SeedAssignmentAsync(options, user, serverId, Permissions.ModRemove);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ModInventoryCache cache = new();
            cache.Record(Inventory(serverId, agent, workshop: ["100"], enabled: ["A"]));
            ServerModManager sut = Manager(db, coordinator, new CapturingAuditWriter(), cache);

            ModManagementResult result = await sut.EnableModsAsync(user, serverId, ["B"]);

            await Assert.That(result.Failure).IsEqualTo(ModManagementFailure.NotAuthorized);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    public async Task Enable_reports_snapshot_unavailable_when_no_inventory_has_been_observed()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ModInstall);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerModManager sut = Manager(db, coordinator, new CapturingAuditWriter(), new ModInventoryCache());

            ModManagementResult result = await sut.EnableModsAsync(user, serverId, ["B"]);

            await Assert.That(result.Failure).IsEqualTo(ModManagementFailure.SnapshotUnavailable);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    public async Task Enable_of_an_already_enabled_mod_reports_no_change()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ModInstall);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ModInventoryCache cache = new();
            cache.Record(Inventory(serverId, agent, workshop: ["100"], enabled: ["A"]));
            ServerModManager sut = Manager(db, coordinator, new CapturingAuditWriter(), cache);

            ModManagementResult result = await sut.EnableModsAsync(user, serverId, ["A"]);

            await Assert.That(result.Failure).IsEqualTo(ModManagementFailure.NoChange);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    public async Task Disable_authorizes_mod_remove_and_drops_from_the_mods_list()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ModRemove);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            CapturingAuditWriter audit = new();
            ModInventoryCache cache = new();
            cache.Record(Inventory(serverId, agent, workshop: ["100"], enabled: ["A", "B"]));
            ServerModManager sut = Manager(db, coordinator, audit, cache);

            ModManagementResult result = await sut.DisableModsAsync(user, serverId, ["B"]);

            await Assert.That(result.Succeeded).IsTrue();
            ConfigApplyPayload payload = ConfigApplyPayload.FromJson(coordinator.LastRequest!.CommandPayload!);
            await Assert.That(payload.Edits[0]).IsEqualTo(new ConfigApplyEdit("Mods", ConfigEditKind.Text, "A"));
            await Assert.That(audit.Actions).Contains(ModAuditActions.Disabled);
        });
    }

    [Test]
    public async Task Disable_denies_when_the_caller_only_has_mod_install()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ModInstall);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ModInventoryCache cache = new();
            cache.Record(Inventory(serverId, agent, workshop: ["100"], enabled: ["A", "B"]));
            ServerModManager sut = Manager(db, coordinator, new CapturingAuditWriter(), cache);

            ModManagementResult result = await sut.DisableModsAsync(user, serverId, ["B"]);

            await Assert.That(result.Failure).IsEqualTo(ModManagementFailure.NotAuthorized);
        });
    }

    [Test]
    public async Task Reorder_that_is_not_a_permutation_is_rejected()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ModInstall);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ModInventoryCache cache = new();
            cache.Record(Inventory(serverId, agent, workshop: ["100"], enabled: ["A", "B"]));
            ServerModManager sut = Manager(db, coordinator, new CapturingAuditWriter(), cache);

            ModManagementResult result = await sut.ReorderModsAsync(user, serverId, ["A", "C"]);

            await Assert.That(result.Failure).IsEqualTo(ModManagementFailure.InvalidReorder);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    public async Task Reorder_rewrites_the_mods_list_to_the_requested_order()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ModInstall);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            CapturingAuditWriter audit = new();
            ModInventoryCache cache = new();
            cache.Record(Inventory(serverId, agent, workshop: ["100"], enabled: ["A", "B", "C"]));
            ServerModManager sut = Manager(db, coordinator, audit, cache);

            ModManagementResult result = await sut.ReorderModsAsync(user, serverId, ["C", "A", "B"]);

            await Assert.That(result.Succeeded).IsTrue();
            ConfigApplyPayload payload = ConfigApplyPayload.FromJson(coordinator.LastRequest!.CommandPayload!);
            await Assert.That(payload.Edits[0]).IsEqualTo(new ConfigApplyEdit("Mods", ConfigEditKind.Text, "C;A;B"));
            await Assert.That(audit.Actions).Contains(ModAuditActions.Reordered);
        });
    }

    [Test]
    public async Task Add_workshop_item_appends_to_workshop_items_only()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ModInstall);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            CapturingAuditWriter audit = new();
            ModInventoryCache cache = new();
            cache.Record(Inventory(serverId, agent, workshop: ["100"], enabled: ["A"]));
            ServerModManager sut = Manager(db, coordinator, audit, cache);

            ModManagementResult result = await sut.AddWorkshopItemAsync(user, serverId, "200");

            await Assert.That(result.Succeeded).IsTrue();
            ConfigApplyPayload payload = ConfigApplyPayload.FromJson(coordinator.LastRequest!.CommandPayload!);
            await Assert.That(payload.Edits.Count).IsEqualTo(1);
            await Assert.That(payload.Edits[0]).IsEqualTo(new ConfigApplyEdit("WorkshopItems", ConfigEditKind.Text, "100;200"));
            await Assert.That(audit.Actions).Contains(ModAuditActions.Installed);
        });
    }

    [Test]
    public async Task Add_workshop_item_rejects_a_non_numeric_id()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ModInstall);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ModInventoryCache cache = new();
            cache.Record(Inventory(serverId, agent, workshop: ["100"], enabled: ["A"]));
            ServerModManager sut = Manager(db, coordinator, new CapturingAuditWriter(), cache);

            ModManagementResult result = await sut.AddWorkshopItemAsync(user, serverId, "not-a-number");

            await Assert.That(result.Failure).IsEqualTo(ModManagementFailure.InvalidInput);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    [Arguments("B;C")]
    [Arguments("B,C")]
    [Arguments("B\nC")]
    public async Task Enable_rejects_a_mod_id_that_would_corrupt_the_mods_list(string modId)
    {
        // #290 D3: a separator inside an id would split Mods= into ids nobody asked for.
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ModInstall);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ModInventoryCache cache = new();
            cache.Record(Inventory(serverId, agent, workshop: ["100"], enabled: ["A"]));
            ServerModManager sut = Manager(db, coordinator, new CapturingAuditWriter(), cache);

            ModManagementResult result = await sut.EnableModsAsync(user, serverId, [modId]);

            await Assert.That(result.Failure).IsEqualTo(ModManagementFailure.InvalidInput);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    public async Task Remove_workshop_item_drops_the_item_and_its_exclusive_mods()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ModRemove);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            CapturingAuditWriter audit = new();
            ModInventoryCache cache = new();
            cache.Record(new ModInventory(
                serverId, agent,
                InstalledItems:
                [
                    new InstalledWorkshopItem("100", [new InstalledMod("A", null)]),
                    new InstalledWorkshopItem("200", [new InstalledMod("B", null)]),
                ],
                ConfiguredWorkshopIds: ["100", "200"],
                EnabledModIds: ["A", "B"],
                Issues: [],
                ObservedAt: Now));
            ServerModManager sut = Manager(db, coordinator, audit, cache);

            ModManagementResult result = await sut.RemoveWorkshopItemsAsync(user, serverId, ["100"]);

            await Assert.That(result.Succeeded).IsTrue();
            ConfigApplyPayload payload = ConfigApplyPayload.FromJson(coordinator.LastRequest!.CommandPayload!);
            await Assert.That(payload.Edits.Count).IsEqualTo(2);
            await Assert.That(payload.Edits[0]).IsEqualTo(new ConfigApplyEdit("WorkshopItems", ConfigEditKind.Text, "200"));
            await Assert.That(payload.Edits[1]).IsEqualTo(new ConfigApplyEdit("Mods", ConfigEditKind.Text, "B"));
            await Assert.That(audit.Actions).Contains(ModAuditActions.Removed);
        });
    }

    [Test]
    public async Task Enable_reports_server_not_found_for_an_unknown_server()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId unknown = ServerId.New();
            await SeedAssignmentAsync(options, user, unknown, Permissions.ModInstall);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerModManager sut = Manager(db, coordinator, new CapturingAuditWriter(), new ModInventoryCache());

            ModManagementResult result = await sut.EnableModsAsync(user, unknown, ["B"]);

            await Assert.That(result.Failure).IsEqualTo(ModManagementFailure.ServerNotFound);
        });
    }

    [Test]
    public async Task Enable_reports_server_busy_when_the_per_server_lock_refuses()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ModInstall);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ModInventoryCache cache = new();
            cache.Record(Inventory(serverId, agent, workshop: ["100"], enabled: ["A"]));
            ServerModManager sut = Manager(db, new RecordingCoordinator { ThrowBusy = true }, new CapturingAuditWriter(), cache);

            ModManagementResult result = await sut.EnableModsAsync(user, serverId, ["B"]);

            await Assert.That(result.Failure).IsEqualTo(ModManagementFailure.ServerBusy);
        });
    }

    [Test]
    public async Task Enable_uses_the_latest_ini_revision_hash_as_the_drift_baseline()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ModInstall);
            await SeedRevisionAsync(options, serverId, PzConfigFile.Ini, "ini-baseline-hash");

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ModInventoryCache cache = new();
            cache.Record(Inventory(serverId, agent, workshop: ["100"], enabled: ["A"]));
            ServerModManager sut = Manager(db, coordinator, new CapturingAuditWriter(), cache);

            await sut.EnableModsAsync(user, serverId, ["B"]);

            ConfigApplyPayload payload = ConfigApplyPayload.FromJson(coordinator.LastRequest!.CommandPayload!);
            await Assert.That(payload.BaselineHash).IsEqualTo("ini-baseline-hash");
        });
    }

    [Test]
    public async Task Update_authorizes_mod_update_and_enqueues_a_safe_restart_not_a_game_update()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ModUpdate);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            CapturingAuditWriter audit = new();
            ServerModManager sut = Manager(db, coordinator, audit, new ModInventoryCache());

            // #273: a restart is what refreshes Workshop content (PZ re-fetches WorkshopItems= at boot), so the refresh no
            // longer runs SteamCMD and can't silently change the game build. No plan ⇒ the Agent's default warning.
            ModManagementResult result = await sut.UpdateModsAsync(user, serverId);

            await Assert.That(result.Succeeded).IsTrue();
            await Assert.That(coordinator.LastRequest!.Kind).IsEqualTo(OperationKind.RestartServer);
            await Assert.That(coordinator.LastRequest!.IsMutating).IsTrue();
            await Assert.That(coordinator.LastRequest!.CommandPayload).IsNull();
            await Assert.That(audit.Actions).Contains(ModAuditActions.Updated);
        });
    }

    [Test]
    public async Task Update_is_also_allowed_with_server_restart_alone()
    {
        // #273: one Mods-section button now does the restart that applies mod changes and pulls Workshop updates, so
        // an operator who may restart the server may press it without a Mod.Update grant.
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerRestart);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerModManager sut = Manager(db, coordinator, new CapturingAuditWriter(), new ModInventoryCache());

            ModManagementResult result = await sut.UpdateModsAsync(user, serverId);

            await Assert.That(result.Succeeded).IsTrue();
            await Assert.That(coordinator.LastRequest!.Kind).IsEqualTo(OperationKind.RestartServer);
        });
    }

    [Test]
    public async Task Update_with_a_countdown_carries_the_graceful_plan_on_the_restart()
    {
        // #292 D3: the pending-changes bar's countdown select rides the restart as the #114 graceful payload.
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ModUpdate);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerModManager sut = Manager(db, coordinator, new CapturingAuditWriter(), new ModInventoryCache());
            GracefulRestartPayload plan = new([60, 30, 10], "Applying mod changes.");

            ModManagementResult result = await sut.UpdateModsAsync(user, serverId, plan);

            await Assert.That(result.Succeeded).IsTrue();
            await Assert.That(coordinator.LastRequest!.CommandPayload).IsEqualTo(plan.ToJson());
        });
    }

    [Test]
    public async Task Update_with_an_invalid_countdown_is_refused_before_anything_is_enqueued()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ModUpdate);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerModManager sut = Manager(db, coordinator, new CapturingAuditWriter(), new ModInventoryCache());

            // Past the 15-minute ceiling: the countdown would pin the per-server lock (GracefulRestartRules).
            ModManagementResult result = await sut.UpdateModsAsync(user, serverId, new GracefulRestartPayload([3600, 60]));

            await Assert.That(result.Failure).IsEqualTo(ModManagementFailure.InvalidInput);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    public async Task Update_denies_without_mod_update_or_server_restart()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ModInstall);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerModManager sut = Manager(db, coordinator, new CapturingAuditWriter(), new ModInventoryCache());

            ModManagementResult result = await sut.UpdateModsAsync(user, serverId);

            await Assert.That(result.Failure).IsEqualTo(ModManagementFailure.NotAuthorized);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    // ---- #291 one-click Install -------------------------------------------------------------------------------

    [Test]
    public async Task Install_enqueues_one_config_apply_writing_workshop_items_and_mods()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ModInstall);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            CapturingAuditWriter audit = new();
            ModInventoryCache cache = new();
            cache.Record(Inventory(serverId, agent, workshop: ["100"], enabled: ["A"]));
            ServerModManager sut = Manager(db, coordinator, audit, cache);

            ModManagementResult result = await sut.InstallWorkshopItemsAsync(user, serverId, ["200"], ["B", "C"]);

            await Assert.That(result.Succeeded).IsTrue();
            await Assert.That(coordinator.LastRequest!.Kind).IsEqualTo(OperationKind.ConfigApply);
            ConfigApplyPayload payload = ConfigApplyPayload.FromJson(coordinator.LastRequest!.CommandPayload!);
            await Assert.That(payload.Edits.Count).IsEqualTo(2);
            await Assert.That(payload.Edits[0]).IsEqualTo(new ConfigApplyEdit("WorkshopItems", ConfigEditKind.Text, "100;200"));
            await Assert.That(payload.Edits[1]).IsEqualTo(new ConfigApplyEdit("Mods", ConfigEditKind.Text, "A;B;C"));
            await Assert.That(audit.Actions).Contains(ModAuditActions.Installed);
        });
    }

    [Test]
    public async Task Install_with_no_mod_ids_only_writes_workshop_items()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ModInstall);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ModInventoryCache cache = new();
            cache.Record(Inventory(serverId, agent, workshop: [], enabled: ["A"]));
            ServerModManager sut = Manager(db, coordinator, new CapturingAuditWriter(), cache);

            ModManagementResult result = await sut.InstallWorkshopItemsAsync(user, serverId, ["200"], []);

            await Assert.That(result.Succeeded).IsTrue();
            ConfigApplyPayload payload = ConfigApplyPayload.FromJson(coordinator.LastRequest!.CommandPayload!);
            await Assert.That(payload.Edits.Count).IsEqualTo(1);
            await Assert.That(payload.Edits[0]).IsEqualTo(new ConfigApplyEdit("WorkshopItems", ConfigEditKind.Text, "200"));
        });
    }

    [Test]
    [Arguments("200", "Bad;Id")]
    [Arguments("2O0", "Fine")]
    public async Task Install_rejects_an_invalid_mod_id_or_workshop_id(string workshopId, string modId)
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ModInstall);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ModInventoryCache cache = new();
            cache.Record(Inventory(serverId, agent, workshop: [], enabled: []));
            ServerModManager sut = Manager(db, coordinator, new CapturingAuditWriter(), cache);

            ModManagementResult result = await sut.InstallWorkshopItemsAsync(user, serverId, [workshopId], [modId]);

            await Assert.That(result.Failure).IsEqualTo(ModManagementFailure.InvalidInput);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    public async Task Install_denies_without_the_server_scoped_mod_install_permission()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ModView);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ModInventoryCache cache = new();
            cache.Record(Inventory(serverId, agent, workshop: [], enabled: []));
            ServerModManager sut = Manager(db, coordinator, new CapturingAuditWriter(), cache);

            ModManagementResult result = await sut.InstallWorkshopItemsAsync(user, serverId, ["200"], ["B"]);

            await Assert.That(result.Failure).IsEqualTo(ModManagementFailure.NotAuthorized);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    public async Task Remove_of_a_not_yet_downloaded_item_also_drops_its_guessed_mod_ids()
    {
        // #291: Install enabled the description's guesses before the download, so Remove must drop them too —
        // except an id another configured item also provides.
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ModRemove);
            await SeedGuessesAsync(options, serverId, "200", "B", "Shared");
            await SeedGuessesAsync(options, serverId, "300", "Shared");

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ModInventoryCache cache = new();
            cache.Record(Inventory(serverId, agent, workshop: ["100", "200", "300"], enabled: ["A", "B", "Shared"]));
            ServerModManager sut = Manager(db, coordinator, new CapturingAuditWriter(), cache);

            ModManagementResult result = await sut.RemoveWorkshopItemsAsync(user, serverId, ["200"]);

            await Assert.That(result.Succeeded).IsTrue();
            ConfigApplyPayload payload = ConfigApplyPayload.FromJson(coordinator.LastRequest!.CommandPayload!);
            await Assert.That(payload.Edits[0]).IsEqualTo(new ConfigApplyEdit("WorkshopItems", ConfigEditKind.Text, "100;300"));
            await Assert.That(payload.Edits[1]).IsEqualTo(new ConfigApplyEdit("Mods", ConfigEditKind.Text, "A;Shared"));
        });
    }

    [Test]
    public async Task Undo_of_a_pending_install_takes_out_the_item_and_its_guessed_ids()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            // Undoing an install takes entries out, so it is a Mod.Remove action.
            await SeedAssignmentAsync(options, user, serverId, Permissions.ModRemove);
            await SeedBootAsync(options, serverId, workshop: ["100"], mods: ["A"]);
            await SeedGuessesAsync(options, serverId, "200", "B");

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            CapturingAuditWriter audit = new();
            ModInventoryCache cache = new();
            cache.Record(Inventory(serverId, agent, workshop: ["100", "200"], enabled: ["A", "B"]));
            ServerModManager sut = Manager(db, coordinator, audit, cache);

            ModManagementResult result = await sut.UndoPendingAsync(user, serverId, "200");

            await Assert.That(result.Succeeded).IsTrue();
            ConfigApplyPayload payload = ConfigApplyPayload.FromJson(coordinator.LastRequest!.CommandPayload!);
            await Assert.That(payload.Edits[0]).IsEqualTo(new ConfigApplyEdit("WorkshopItems", ConfigEditKind.Text, "100"));
            await Assert.That(payload.Edits[1]).IsEqualTo(new ConfigApplyEdit("Mods", ConfigEditKind.Text, "A"));
            await Assert.That(audit.Actions).Contains(ModAuditActions.Undone);
        });
    }

    [Test]
    public async Task Undo_of_a_pending_remove_needs_mod_install()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ModRemove);
            await SeedBootAsync(options, serverId, workshop: ["100", "200"], mods: ["A", "B"]);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ModInventoryCache cache = new();
            cache.Record(Inventory(serverId, agent, workshop: ["100"], enabled: ["A"]));
            ServerModManager sut = Manager(db, coordinator, new CapturingAuditWriter(), cache);

            ModManagementResult result = await sut.UndoPendingAsync(user, serverId, "200");

            // Re-adding entries is an install; a Mod.Remove-only grant can't do it.
            await Assert.That(result.Failure).IsEqualTo(ModManagementFailure.NotAuthorized);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    public async Task Undo_before_the_first_recorded_boot_is_invalid()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ModInstall);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ModInventoryCache cache = new();
            cache.Record(Inventory(serverId, agent, workshop: [], enabled: []));
            ServerModManager sut = Manager(db, coordinator, new CapturingAuditWriter(), cache);

            ModManagementResult result = await sut.UndoPendingAsync(user, serverId, "200");

            await Assert.That(result.Failure).IsEqualTo(ModManagementFailure.InvalidInput);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    public async Task Set_parts_swaps_a_wrong_guess_for_the_real_part_in_one_apply()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ModInstall);
            await SeedGuessesAsync(options, serverId, "200", "Guess");

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            CapturingAuditWriter audit = new();
            ModInventoryCache cache = new();
            cache.Record(Inventory(serverId, agent, workshop: ["200"], enabled: ["Guess"]) with
            {
                InstalledItems = [new InstalledWorkshopItem("200", [new InstalledMod("Real", null)])],
            });
            ServerModManager sut = Manager(db, coordinator, audit, cache);

            ModManagementResult result = await sut.SetItemPartsAsync(user, serverId, "200", ["Real"]);

            await Assert.That(result.Succeeded).IsTrue();
            ConfigApplyPayload payload = ConfigApplyPayload.FromJson(coordinator.LastRequest!.CommandPayload!);
            await Assert.That(payload.Edits.Single()).IsEqualTo(new ConfigApplyEdit("Mods", ConfigEditKind.Text, "Real"));
            await Assert.That(audit.Actions).Contains(ModAuditActions.PartsSet);
        });
    }

    [Test]
    public async Task Set_parts_refuses_an_id_the_item_does_not_provide()
    {
        // Pick parts chooses among the item's own parts; an arbitrary id goes through Enable instead.
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ModInstall);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ModInventoryCache cache = new();
            cache.Record(Inventory(serverId, agent, workshop: ["200"], enabled: []) with
            {
                InstalledItems = [new InstalledWorkshopItem("200", [new InstalledMod("Real", null)])],
            });
            ServerModManager sut = Manager(db, coordinator, new CapturingAuditWriter(), cache);

            ModManagementResult result = await sut.SetItemPartsAsync(user, serverId, "200", ["Elsewhere"]);

            await Assert.That(result.Failure).IsEqualTo(ModManagementFailure.InvalidInput);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    public async Task Delete_downloads_enqueues_a_mutating_delete_of_unused_items_and_audits_it()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ModRemove);
            await SeedStateAsync(options, serverId, booted: ["100"], configured: ["100"]);
            await SeedOnDiskAsync(options, serverId, "100", "200", "300");

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            CapturingAuditWriter audit = new();
            ServerModManager sut = Manager(db, coordinator, audit, new ModInventoryCache());

            ModManagementResult result = await sut.DeleteDownloadsAsync(user, serverId, ["200", "300"]);

            await Assert.That(result.Succeeded).IsTrue();
            await Assert.That(coordinator.LastRequest!.Kind).IsEqualTo(OperationKind.DeleteWorkshopContent);
            await Assert.That(coordinator.LastRequest!.IsMutating).IsTrue();
            await Assert.That(coordinator.LastRequest!.ServerId).IsEqualTo(serverId);
            await Assert.That(coordinator.LastRequest!.AgentId).IsEqualTo(agent);
            await Assert.That(WorkshopContentCommandPayload.FromJson(coordinator.LastRequest!.CommandPayload!).WorkshopIds)
                .IsEquivalentTo(["200", "300"]);
            await Assert.That(audit.Actions).Contains(ModAuditActions.DownloadsDeleted);
        });
    }

    [Test]
    public async Task Delete_downloads_refuses_an_item_the_server_still_uses()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ModRemove);
            await SeedStateAsync(options, serverId, booted: ["100"], configured: ["100"]);
            await SeedOnDiskAsync(options, serverId, "100", "200");

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            CapturingAuditWriter audit = new();
            ServerModManager sut = Manager(db, coordinator, audit, new ModInventoryCache());

            ModManagementResult result = await sut.DeleteDownloadsAsync(user, serverId, ["200", "100"]);

            await Assert.That(result.Failure).IsEqualTo(ModManagementFailure.InvalidInput);
            await Assert.That(coordinator.LastRequest).IsNull();
            await Assert.That(audit.Actions).Contains(ModAuditActions.DownloadsDeleted);
        });
    }

    [Test]
    public async Task Delete_downloads_refuses_an_item_removed_since_the_last_boot()
    {
        // Removed from WorkshopItems= but loaded at the last boot (RemovedOnRestart): PZ may still read its files.
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ModRemove);
            await SeedStateAsync(options, serverId, booted: ["100", "200"], configured: ["100"]);
            await SeedOnDiskAsync(options, serverId, "100", "200");

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerModManager sut = Manager(db, coordinator, new CapturingAuditWriter(), new ModInventoryCache());

            ModManagementResult result = await sut.DeleteDownloadsAsync(user, serverId, ["200"]);

            await Assert.That(result.Failure).IsEqualTo(ModManagementFailure.InvalidInput);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    public async Task Delete_downloads_denies_without_the_server_scoped_mod_remove_permission()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ModInstall);
            await SeedStateAsync(options, serverId, booted: [], configured: []);
            await SeedOnDiskAsync(options, serverId, "200");

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerModManager sut = Manager(db, coordinator, new CapturingAuditWriter(), new ModInventoryCache());

            ModManagementResult result = await sut.DeleteDownloadsAsync(user, serverId, ["200"]);

            await Assert.That(result.Failure).IsEqualTo(ModManagementFailure.NotAuthorized);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    [Arguments("12a")]
    [Arguments("../1")]
    [Arguments("")]
    public async Task Delete_downloads_rejects_an_id_that_is_not_numeric(string bad)
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ModRemove);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerModManager sut = Manager(db, coordinator, new CapturingAuditWriter(), new ModInventoryCache());

            ModManagementResult result = await sut.DeleteDownloadsAsync(user, serverId, [bad]);

            await Assert.That(result.Failure).IsEqualTo(ModManagementFailure.InvalidInput);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    public async Task Delete_downloads_reports_a_busy_server()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ModRemove);
            await SeedStateAsync(options, serverId, booted: [], configured: []);
            await SeedOnDiskAsync(options, serverId, "200");

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerModManager sut = Manager(
                db, new RecordingCoordinator { ThrowBusy = true }, new CapturingAuditWriter(), new ModInventoryCache());

            ModManagementResult result = await sut.DeleteDownloadsAsync(user, serverId, ["200"]);

            await Assert.That(result.Failure).IsEqualTo(ModManagementFailure.ServerBusy);
        });
    }

    private static async Task SeedStateAsync(DbContextOptions options, ServerId server, string[] booted, string[] configured)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
        ServerModState state = ServerModState.For(server);
        state.MarkBooted(Now);
        state.ObserveConfig(booted, [], Now.AddSeconds(5));
        state.ObserveConfig(configured, [], Now.AddSeconds(10));
        db.Set<ServerModState>().Add(state);
        await db.SaveChangesAsync();
    }

    private static async Task SeedOnDiskAsync(DbContextOptions options, ServerId server, params string[] workshopIds)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
        foreach (string workshopId in workshopIds)
        {
            ServerWorkshopItem item = ServerWorkshopItem.Track(server, workshopId);
            item.ObserveDisk(true, [], Now);
            db.Set<ServerWorkshopItem>().Add(item);
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedBootAsync(DbContextOptions options, ServerId server, string[] workshop, string[] mods)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
        ServerModState state = ServerModState.For(server);
        state.MarkBooted(Now);
        state.ObserveConfig(workshop, mods, Now.AddSeconds(5));
        db.Set<ServerModState>().Add(state);
        await db.SaveChangesAsync();
    }

    private static async Task SeedGuessesAsync(DbContextOptions options, ServerId server, string workshopId, params string[] guesses)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
        ServerWorkshopItem item = ServerWorkshopItem.Track(server, workshopId);
        item.ApplyMetadata(
            "title", null, null, null, [],
            [.. guesses.Select(g => PzModId.TryCreate(g, out PzModId id) ? id : throw new ArgumentException(g))], Now);
        db.Set<ServerWorkshopItem>().Add(item);
        await db.SaveChangesAsync();
    }

    private static ModInventory Inventory(
        ServerId server, AgentId agent, IReadOnlyList<string> workshop, IReadOnlyList<string> enabled) =>
        new(server, agent, InstalledItems: [], ConfiguredWorkshopIds: workshop, EnabledModIds: enabled, Issues: [], ObservedAt: Now);

    private static ServerModManager Manager(
        ZWardenDbContext db, RecordingCoordinator coordinator, CapturingAuditWriter audit, ModInventoryCache cache)
        => new(
            new ServerRepository(db),
            new PermissionChecker(db, new TestTenantContext(Tenant)),
            cache,
            coordinator,
            new ConfigurationRevisionRepository(db),
            audit,
            new ServerWorkshopItemRepository(db),
            new ServerModStateRepository(db));

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
            ConfigurationRevision.Record(server, file, "[[\"Mods\",\"s:A\"]]", hash, Now));
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
