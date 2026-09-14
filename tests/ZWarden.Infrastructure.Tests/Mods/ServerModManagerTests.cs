using Microsoft.EntityFrameworkCore;
using ZWarden.Application.Configuration;
using ZWarden.Application.Mods;
using ZWarden.Application.Operations;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;
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
/// F22: the mod-management service, fail-closed (ADR 0018). Config-as-truth — enable/disable/reorder/add/remove
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
    public async Task Update_authorizes_mod_update_and_enqueues_an_update_server_operation()
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

            ModManagementResult result = await sut.UpdateModsAsync(user, serverId);

            await Assert.That(result.Succeeded).IsTrue();
            await Assert.That(coordinator.LastRequest!.Kind).IsEqualTo(OperationKind.UpdateServer);
            await Assert.That(coordinator.LastRequest!.IsMutating).IsTrue();
            await Assert.That(coordinator.LastRequest!.CommandPayload).IsNull();
            await Assert.That(audit.Actions).Contains(ModAuditActions.Updated);
        });
    }

    [Test]
    public async Task Update_denies_without_the_mod_update_permission()
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
            audit);

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
