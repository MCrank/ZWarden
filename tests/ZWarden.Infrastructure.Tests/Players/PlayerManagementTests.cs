using Microsoft.EntityFrameworkCore;
using ZWarden.Application.Operations;
using ZWarden.Application.Players;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Players;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Players;
using ZWarden.Infrastructure.Servers;
using ZWarden.Infrastructure.Tests.Agents;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Players;

/// <summary>
/// F19: the player-management service, fail-closed (ADR 0018). Each action authorizes its own server-scoped
/// permission against the specific Server, resolves the Server through the tenant filter (foreign/unknown ⇒
/// ServerNotFound), validates the operator's arguments before enqueuing (D-3), and enqueues a <b>non-mutating</b>,
/// server-scoped Operation carrying the target parameters in its command payload. A ban writes an advisory
/// registry record; an unban lifts it (ADR 0027). Proven against a real SQLite database, a real
/// <see cref="PermissionChecker"/>, and seeded assignments.
/// </summary>
public class PlayerManagementTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task List_players_enqueues_a_non_mutating_enumeration_and_is_not_audited()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.PlayerView);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            CapturingAuditWriter audit = new();
            PlayerManagement sut = Management(db, coordinator, audit);

            PlayerManagementResult result = await sut.ListPlayersAsync(user, serverId);

            await Assert.That(result.Succeeded).IsTrue();
            await Assert.That(coordinator.LastRequest!.Kind).IsEqualTo(OperationKind.ListPlayers);
            await Assert.That(coordinator.LastRequest!.IsMutating).IsFalse();
            await Assert.That(coordinator.LastRequest!.CommandPayload).IsNull();
            // Enumeration is a read, not administrative activity — not audited.
            await Assert.That(audit.Actions).IsEmpty();
        });
    }

    [Test]
    public async Task List_players_denies_without_the_view_permission()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            // No Player.View assignment.

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            PlayerManagement sut = Management(db, coordinator, new CapturingAuditWriter());

            PlayerManagementResult result = await sut.ListPlayersAsync(user, serverId);

            await Assert.That(result.Failure).IsEqualTo(PlayerManagementFailure.NotAuthorized);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    public async Task Kick_enqueues_a_non_mutating_kick_carrying_the_username_and_reason()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.PlayerKick);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            CapturingAuditWriter audit = new();
            PlayerManagement sut = Management(db, coordinator, audit);

            PlayerManagementResult result = await sut.KickAsync(user, serverId, "Bob", "griefing");

            await Assert.That(result.Succeeded).IsTrue();
            await Assert.That(coordinator.LastRequest!.Kind).IsEqualTo(OperationKind.KickPlayer);
            await Assert.That(coordinator.LastRequest!.IsMutating).IsFalse();
            await Assert.That(coordinator.LastRequest!.ServerId).IsEqualTo(serverId);
            await Assert.That(coordinator.LastRequest!.AgentId).IsEqualTo(agent);
            PlayerCommandPayload payload = PlayerCommandPayload.FromJson(coordinator.LastRequest!.CommandPayload!);
            await Assert.That(payload.Username).IsEqualTo("Bob");
            await Assert.That(payload.Reason).IsEqualTo("griefing");
            await Assert.That(audit.Actions).Contains(PlayerAuditActions.Kicked);
        });
    }

    [Test]
    public async Task Ban_enqueues_and_records_an_active_ban()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.PlayerBan);

            await using (ZWardenDbContext db = new(options, new TestTenantContext(Tenant)))
            {
                RecordingCoordinator coordinator = new();
                CapturingAuditWriter audit = new();
                PlayerManagement sut = Management(db, coordinator, audit);

                PlayerManagementResult result = await sut.BanAsync(user, serverId, "Mallory", "cheating");

                await Assert.That(result.Succeeded).IsTrue();
                await Assert.That(coordinator.LastRequest!.Kind).IsEqualTo(OperationKind.BanPlayer);
                await Assert.That(audit.Actions).Contains(PlayerAuditActions.Banned);
            }

            IReadOnlyList<BanRecord> bans = await BansAsync(options, serverId);
            await Assert.That(bans.Count).IsEqualTo(1);
            await Assert.That(bans[0].Username).IsEqualTo("Mallory");
            await Assert.That(bans[0].Reason).IsEqualTo("cheating");
            await Assert.That(bans[0].Status).IsEqualTo(BanStatus.Active);
            await Assert.That(bans[0].IssuedByUserId).IsEqualTo(user);
        });
    }

    [Test]
    public async Task Banning_the_same_user_twice_keeps_one_active_record()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.PlayerBan);

            await using (ZWardenDbContext db = new(options, new TestTenantContext(Tenant)))
            {
                PlayerManagement sut = Management(db, new RecordingCoordinator(), new CapturingAuditWriter());
                await sut.BanAsync(user, serverId, "Mallory", "first");
            }

            await using (ZWardenDbContext db = new(options, new TestTenantContext(Tenant)))
            {
                PlayerManagement sut = Management(db, new RecordingCoordinator(), new CapturingAuditWriter());
                await sut.BanAsync(user, serverId, "Mallory", "second");
            }

            IReadOnlyList<BanRecord> bans = await BansAsync(options, serverId);
            await Assert.That(bans.Count(b => b.Status == BanStatus.Active)).IsEqualTo(1);
        });
    }

    [Test]
    public async Task Unban_lifts_the_active_record()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.PlayerBan);
            await SeedAssignmentAsync(options, user, serverId, Permissions.PlayerUnban);

            await using (ZWardenDbContext db = new(options, new TestTenantContext(Tenant)))
            {
                PlayerManagement sut = Management(db, new RecordingCoordinator(), new CapturingAuditWriter());
                await sut.BanAsync(user, serverId, "Mallory", null);
            }

            await using (ZWardenDbContext db = new(options, new TestTenantContext(Tenant)))
            {
                RecordingCoordinator coordinator = new();
                CapturingAuditWriter audit = new();
                PlayerManagement sut = Management(db, coordinator, audit);
                PlayerManagementResult result = await sut.UnbanAsync(user, serverId, "Mallory");

                await Assert.That(result.Succeeded).IsTrue();
                await Assert.That(coordinator.LastRequest!.Kind).IsEqualTo(OperationKind.UnbanPlayer);
                await Assert.That(audit.Actions).Contains(PlayerAuditActions.Unbanned);
            }

            IReadOnlyList<BanRecord> bans = await BansAsync(options, serverId);
            await Assert.That(bans.Count).IsEqualTo(1);
            await Assert.That(bans[0].Status).IsEqualTo(BanStatus.Lifted);
            await Assert.That(bans[0].LiftedByUserId).IsEqualTo(user);
        });
    }

    [Test]
    public async Task Remove_from_whitelist_enqueues_under_the_ban_permission()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.PlayerBan);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            CapturingAuditWriter audit = new();
            PlayerManagement sut = Management(db, coordinator, audit);

            PlayerManagementResult result = await sut.RemoveFromWhitelistAsync(user, serverId, "Bob");

            await Assert.That(result.Succeeded).IsTrue();
            await Assert.That(coordinator.LastRequest!.Kind).IsEqualTo(OperationKind.RemoveFromWhitelist);
            await Assert.That(audit.Actions).Contains(PlayerAuditActions.RemovedFromWhitelist);
        });
    }

    [Test]
    public async Task Set_whitelist_mode_enqueues_under_the_config_edit_permission_and_carries_the_flag()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerConfigurationEdit);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            CapturingAuditWriter audit = new();
            PlayerManagement sut = Management(db, coordinator, audit);

            PlayerManagementResult result = await sut.SetWhitelistModeAsync(user, serverId, open: false);

            await Assert.That(result.Succeeded).IsTrue();
            await Assert.That(coordinator.LastRequest!.Kind).IsEqualTo(OperationKind.SetWhitelistMode);
            await Assert.That(PlayerCommandPayload.FromJson(coordinator.LastRequest!.CommandPayload!).Open!.Value).IsFalse();
            await Assert.That(audit.Actions).Contains(PlayerAuditActions.WhitelistModeChanged);
        });
    }

    [Test]
    public async Task Kick_denies_without_the_server_scoped_kick_permission()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            // A kick grant on a different server does not authorize this one.
            await SeedAssignmentAsync(options, user, ServerId.New(), Permissions.PlayerKick);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            PlayerManagement sut = Management(db, coordinator, new CapturingAuditWriter());

            PlayerManagementResult result = await sut.KickAsync(user, serverId, "Bob", null);

            await Assert.That(result.Failure).IsEqualTo(PlayerManagementFailure.NotAuthorized);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    public async Task A_kick_grant_does_not_authorize_a_ban()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.PlayerKick);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            PlayerManagement sut = Management(db, new RecordingCoordinator(), new CapturingAuditWriter());

            PlayerManagementResult result = await sut.BanAsync(user, serverId, "Bob", null);

            await Assert.That(result.Failure).IsEqualTo(PlayerManagementFailure.NotAuthorized);
        });
    }

    [Test]
    public async Task Set_whitelist_mode_denies_without_config_edit()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.PlayerBan); // not config edit

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            PlayerManagement sut = Management(db, new RecordingCoordinator(), new CapturingAuditWriter());

            PlayerManagementResult result = await sut.SetWhitelistModeAsync(user, serverId, open: true);

            await Assert.That(result.Failure).IsEqualTo(PlayerManagementFailure.NotAuthorized);
        });
    }

    [Test]
    public async Task Kick_reports_server_not_found_for_an_unknown_server()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId unknown = ServerId.New();
            await SeedAssignmentAsync(options, user, unknown, Permissions.PlayerKick);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            PlayerManagement sut = Management(db, new RecordingCoordinator(), new CapturingAuditWriter());

            PlayerManagementResult result = await sut.KickAsync(user, unknown, "Bob", null);

            await Assert.That(result.Failure).IsEqualTo(PlayerManagementFailure.ServerNotFound);
        });
    }

    [Test]
    public async Task An_injection_username_is_rejected_before_enqueue()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.PlayerKick);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            PlayerManagement sut = Management(db, coordinator, new CapturingAuditWriter());

            PlayerManagementResult result = await sut.KickAsync(user, serverId, "Bob\" -r \"x", null);

            await Assert.That(result.Failure).IsEqualTo(PlayerManagementFailure.InvalidInput);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    public async Task An_injection_reason_is_rejected_before_enqueue()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.PlayerBan);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            PlayerManagement sut = Management(db, coordinator, new CapturingAuditWriter());

            PlayerManagementResult result = await sut.BanAsync(user, serverId, "Bob", "he said \"hi\"");

            await Assert.That(result.Failure).IsEqualTo(PlayerManagementFailure.InvalidInput);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    private static PlayerManagement Management(ZWardenDbContext db, RecordingCoordinator coordinator, CapturingAuditWriter audit)
        => new(
            new ServerRepository(db),
            new PermissionChecker(db, new TestTenantContext(Tenant)),
            coordinator,
            audit,
            new BanRecordRepository(db),
            db,
            TimeProvider.System);

    private sealed class RecordingCoordinator : IOperationCoordinator
    {
        public EnqueueOperationRequest? LastRequest { get; private set; }

        public Task<Operation> EnqueueAsync(
            EnqueueOperationRequest request,
            UserId? actor = null,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(Operation.Enqueue(
                request.AgentId, request.Kind, request.IsMutating, request.IdempotencyKey, Now, request.ServerId,
                request.CommandPayload));
        }

        public Task<Operation> RequestCancellationAsync(
            OperationId operationId,
            UserId? actor = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static async Task<IReadOnlyList<BanRecord>> BansAsync(DbContextOptions options, ServerId server)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
        return await new BanRecordRepository(db).ListForServerAsync(server);
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
        DbContextOptions options,
        UserId user,
        ServerId server,
        PermissionDefinition permission)
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
