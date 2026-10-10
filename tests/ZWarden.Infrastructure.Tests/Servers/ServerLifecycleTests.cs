using Microsoft.EntityFrameworkCore;
using ZWarden.Application.Audit;
using ZWarden.Application.Operations;
using ZWarden.Application.Servers;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Servers;
using ZWarden.Infrastructure.Tests.Agents;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Servers;

/// <summary>
/// F15: the lifecycle service, fail-closed (ADR 0018). Each verb authorizes its own <b>server-scoped</b>
/// permission against the specific Server, resolves the Server through the tenant filter (foreign/unknown ⇒
/// ServerNotFound), audits, and enqueues a mutating, server-scoped Operation on the Server's Agent; a busy
/// server (per-server lock, ADR 0022) surfaces as ServerBusy. Proven against a real SQLite database, a real
/// <see cref="PermissionChecker"/>, and seeded assignments.
/// </summary>
public class ServerLifecycleTests
{
    private const long GiB = 1024L * 1024 * 1024;
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Start_enqueues_a_mutating_start_operation_on_the_servers_agent()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerStart);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            CapturingAuditWriter audit = new();
            ServerLifecycle sut = Lifecycle(db, coordinator, audit);

            ServerLifecycleResult result = await sut.StartAsync(user, serverId);

            await Assert.That(result.Succeeded).IsTrue();
            await Assert.That(result.Operation).IsNotNull();
            await Assert.That(coordinator.LastRequest!.Kind).IsEqualTo(OperationKind.StartServer);
            await Assert.That(coordinator.LastRequest!.IsMutating).IsTrue();
            await Assert.That(coordinator.LastRequest!.ServerId).IsEqualTo(serverId);
            await Assert.That(coordinator.LastRequest!.AgentId).IsEqualTo(agent);
            await Assert.That(audit.Actions).Contains(ServerAuditActions.Started);
        });
    }

    [Test]
    public async Task Stop_and_restart_enqueue_their_own_kinds()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerStop);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerRestart);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerLifecycle sut = Lifecycle(db, coordinator, new CapturingAuditWriter());

            ServerLifecycleResult stop = await sut.StopAsync(user, serverId);
            await Assert.That(stop.Succeeded).IsTrue();
            await Assert.That(coordinator.LastRequest!.Kind).IsEqualTo(OperationKind.StopServer);

            ServerLifecycleResult restart = await sut.RestartAsync(user, serverId);
            await Assert.That(restart.Succeeded).IsTrue();
            await Assert.That(coordinator.LastRequest!.Kind).IsEqualTo(OperationKind.RestartServer);
        });
    }

    [Test]
    public async Task Update_enqueues_the_update_kind_and_audits_it()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerUpdate);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            CapturingAuditWriter audit = new();
            ServerLifecycle sut = Lifecycle(db, coordinator, audit);

            ServerLifecycleResult update = await sut.UpdateAsync(user, serverId);

            await Assert.That(update.Succeeded).IsTrue();
            await Assert.That(coordinator.LastRequest!.Kind).IsEqualTo(OperationKind.UpdateServer);
            await Assert.That(coordinator.LastRequest!.IsMutating).IsTrue();
            await Assert.That(audit.Actions).Contains(ServerAuditActions.Updated);
        });
    }

    [Test]
    public async Task Update_denies_without_the_server_scoped_update_permission()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            // No Server.Update assignment.

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerLifecycle sut = Lifecycle(db, new RecordingCoordinator(), new CapturingAuditWriter());

            ServerLifecycleResult update = await sut.UpdateAsync(user, serverId);

            await Assert.That(update.Succeeded).IsFalse();
            await Assert.That(update.Failure).IsEqualTo(ServerLifecycleFailure.NotAuthorized);
        });
    }

    [Test]
    public async Task Start_denies_without_the_server_scoped_start_permission()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            // A start grant on a *different* server does not authorize this one.
            await SeedAssignmentAsync(options, user, ServerId.New(), Permissions.ServerStart);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerLifecycle sut = Lifecycle(db, coordinator, new CapturingAuditWriter());

            ServerLifecycleResult result = await sut.StartAsync(user, serverId);

            await Assert.That(result.Succeeded).IsFalse();
            await Assert.That(result.Failure).IsEqualTo(ServerLifecycleFailure.NotAuthorized);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    public async Task Stop_grant_does_not_authorize_start()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerStop);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerLifecycle sut = Lifecycle(db, new RecordingCoordinator(), new CapturingAuditWriter());

            ServerLifecycleResult result = await sut.StartAsync(user, serverId);

            await Assert.That(result.Failure).IsEqualTo(ServerLifecycleFailure.NotAuthorized);
        });
    }

    [Test]
    public async Task Start_reports_server_not_found_for_an_unknown_server()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId unknown = ServerId.New();
            await SeedAssignmentAsync(options, user, unknown, Permissions.ServerStart);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerLifecycle sut = Lifecycle(db, new RecordingCoordinator(), new CapturingAuditWriter());

            ServerLifecycleResult result = await sut.StartAsync(user, unknown);

            await Assert.That(result.Failure).IsEqualTo(ServerLifecycleFailure.ServerNotFound);
        });
    }

    [Test]
    public async Task Start_reports_server_busy_when_the_per_server_lock_refuses()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerStart);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new() { ThrowBusy = true };
            CapturingAuditWriter audit = new();
            ServerLifecycle sut = Lifecycle(db, coordinator, audit);

            ServerLifecycleResult result = await sut.StartAsync(user, serverId);

            await Assert.That(result.Failure).IsEqualTo(ServerLifecycleFailure.ServerBusy);
        });
    }

    [Test]
    public async Task Recreate_enqueues_a_mutating_recreate_carrying_the_port_and_plan_and_audits_it()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent, gamePort: 16261);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerRecreate);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            CapturingAuditWriter audit = new();
            ServerLifecycle sut = Lifecycle(db, coordinator, audit);

            ServerLifecycleResult result = await sut.RecreateAsync(
                user, serverId, 27015, new GracefulRestartPayload([60, 10], "Changing ports."));

            await Assert.That(result.Succeeded).IsTrue();
            await Assert.That(coordinator.LastRequest!.Kind).IsEqualTo(OperationKind.RecreateServer);
            await Assert.That(coordinator.LastRequest!.IsMutating).IsTrue();
            await Assert.That(coordinator.LastRequest!.ServerId).IsEqualTo(serverId);
            ServerContainerPayload payload = ServerContainerPayload.FromJson(coordinator.LastRequest!.CommandPayload!);
            await Assert.That(payload.GamePort).IsEqualTo(27015);
            await Assert.That(payload.Plan!.WarningLeadSeconds).IsEquivalentTo([60, 10]);
            await Assert.That(audit.Actions).Contains(ServerAuditActions.Recreated);
        });
    }

    [Test]
    public async Task Recreate_without_a_port_keeps_the_current_pair()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New(), gamePort: 16261);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerRecreate);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerLifecycleResult result = await Lifecycle(db, coordinator, new CapturingAuditWriter())
                .RecreateAsync(user, serverId, gamePort: null, plan: null);

            await Assert.That(result.Succeeded).IsTrue();
            await Assert.That(ServerContainerPayload.FromJson(coordinator.LastRequest!.CommandPayload!).GamePort).IsNull();
        });
    }

    [Test]
    public async Task Recreate_denies_without_the_server_scoped_recreate_permission()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            // Restart is not enough — recreate is its own, higher capability (D2).
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerRestart);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerLifecycleResult result = await Lifecycle(db, coordinator, new CapturingAuditWriter())
                .RecreateAsync(user, serverId, 27015, null);

            await Assert.That(result.Failure).IsEqualTo(ServerLifecycleFailure.NotAuthorized);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    [Arguments(80)]
    [Arguments(65535)]
    public async Task Recreate_rejects_a_game_port_out_of_range(int port)
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerRecreate);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerLifecycleResult result = await Lifecycle(db, coordinator, new CapturingAuditWriter())
                .RecreateAsync(user, serverId, port, null);

            await Assert.That(result.Failure).IsEqualTo(ServerLifecycleFailure.InvalidPort);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    public async Task Recreate_carries_a_new_heap_to_the_operation()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerRecreate);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerLifecycleResult result = await Lifecycle(db, coordinator, new CapturingAuditWriter())
                .RecreateAsync(user, serverId, null, null, heapSizeBytes: 8L * 1024 * 1024 * 1024);

            await Assert.That(result.Succeeded).IsTrue();
            await Assert.That(ServerContainerPayload.FromJson(coordinator.LastRequest!.CommandPayload!).HeapSizeBytes)
                .IsEqualTo(8L * 1024 * 1024 * 1024);
        });
    }

    [Test]
    public async Task Recreate_rejects_an_invalid_heap()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerRecreate);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerLifecycleResult result = await Lifecycle(db, coordinator, new CapturingAuditWriter())
                .RecreateAsync(user, serverId, null, null, heapSizeBytes: 1024L * 1024 * 1024);

            await Assert.That(result.Failure).IsEqualTo(ServerLifecycleFailure.InvalidHeap);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    public async Task Recreate_raising_the_heap_past_the_hosts_free_memory_needs_an_acknowledgement()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent, gamePort: 16261, heapSizeBytes: 4 * GiB);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerRecreate);
            HostCapacityCache capacity = new();
            capacity.Record(new HostCapacity(agent, 16 * GiB, 20 * GiB, 6 * GiB, 4 * GiB, 2 * GiB, Now)); // 4 GiB back

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerLifecycle lifecycle = Lifecycle(db, coordinator, new CapturingAuditWriter(), capacity);

            ServerLifecycleResult refused = await lifecycle.RecreateAsync(user, serverId, null, null, heapSizeBytes: 8 * GiB);
            await Assert.That(refused.Failure).IsEqualTo(ServerLifecycleFailure.OverCapacity);
            await Assert.That(coordinator.LastRequest).IsNull();

            ServerLifecycleResult acknowledged = await lifecycle.RecreateAsync(
                user, serverId, null, null, heapSizeBytes: 8 * GiB, acknowledgeOvercommit: true);
            await Assert.That(acknowledged.Succeeded).IsTrue();
        });
    }

    [Test]
    public async Task Recreate_lowering_the_heap_or_keeping_it_is_not_capacity_checked()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent, gamePort: 16261, heapSizeBytes: 8 * GiB);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerRecreate);
            HostCapacityCache capacity = new();
            capacity.Record(new HostCapacity(agent, 16 * GiB, 40 * GiB, 6 * GiB, 4 * GiB, 2 * GiB, Now)); // badly over

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerLifecycle lifecycle = Lifecycle(db, new RecordingCoordinator(), new CapturingAuditWriter(), capacity);

            await Assert.That((await lifecycle.RecreateAsync(user, serverId, null, null, heapSizeBytes: 6 * GiB)).Succeeded).IsTrue();
            await Assert.That((await lifecycle.RecreateAsync(user, serverId, 27015, null)).Succeeded).IsTrue();
        });
    }

    [Test]
    public async Task Recreate_rejects_a_pair_overlapping_another_server_on_the_same_host()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent, gamePort: 16261);
            await SeedServerAsync(options, agent, gamePort: 27016); // 27016/27017 — overlaps 27015/27016
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerRecreate);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerLifecycleResult result = await Lifecycle(db, coordinator, new CapturingAuditWriter())
                .RecreateAsync(user, serverId, 27015, null);

            await Assert.That(result.Failure).IsEqualTo(ServerLifecycleFailure.PortInUse);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    public async Task Recreate_allows_its_own_pair_and_a_pair_used_on_another_host()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New(), gamePort: 27015);
            await SeedServerAsync(options, AgentId.New(), gamePort: 27015); // a different host — no clash
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerRecreate);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerLifecycleResult result = await Lifecycle(db, new RecordingCoordinator(), new CapturingAuditWriter())
                .RecreateAsync(user, serverId, 27015, null);

            await Assert.That(result.Succeeded).IsTrue();
        });
    }

    // --- #271: delete ------------------------------------------------------------------------------------------

    [Test]
    public async Task Delete_with_the_matching_name_enqueues_a_mutating_delete_carrying_the_plan_and_audits_the_name()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerDelete);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            CapturingAuditWriter audit = new();

            ServerLifecycleResult result = await Lifecycle(db, coordinator, audit)
                .DeleteAsync(user, serverId, "survivors", new GracefulRestartPayload([60], "Retiring this server."));

            await Assert.That(result.Succeeded).IsTrue();
            await Assert.That(coordinator.LastRequest!.Kind).IsEqualTo(OperationKind.DeleteServer);
            await Assert.That(coordinator.LastRequest!.IsMutating).IsTrue();
            await Assert.That(coordinator.LastRequest!.AgentId).IsEqualTo(agent);
            await Assert.That(coordinator.LastRequest!.ServerId).IsEqualTo(serverId);
            GracefulRestartPayload plan = GracefulRestartPayload.FromJson(coordinator.LastRequest!.CommandPayload!);
            await Assert.That(plan.WarningLeadSeconds).IsEquivalentTo([60]);
            AuditEntry entry = audit.Entries.Single();
            await Assert.That(entry.Action).IsEqualTo(ServerAuditActions.Deleted);
            // The audit has to read after the row is gone, so it carries the name.
            await Assert.That(entry.Detail).Contains("survivors");
        });
    }

    [Test]
    [Arguments("")]
    [Arguments("Survivors")]
    [Arguments("survivors ")]
    [Arguments("other")]
    public async Task Delete_refuses_a_confirmation_that_is_not_exactly_the_servers_name(string confirmName)
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerDelete);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            CapturingAuditWriter audit = new();
            ServerLifecycleResult result = await Lifecycle(db, coordinator, audit)
                .DeleteAsync(user, serverId, confirmName, plan: null);

            await Assert.That(result.Failure).IsEqualTo(ServerLifecycleFailure.ConfirmationMismatch);
            await Assert.That(coordinator.LastRequest).IsNull();
            await Assert.That(audit.Entries.Single().Outcome).IsEqualTo(AuditOutcome.Denied);
        });
    }

    [Test]
    public async Task Delete_denies_without_the_server_scoped_delete_permission()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            // Recreate is not enough — delete destroys things, so it is its own capability.
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerRecreate);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            CapturingAuditWriter audit = new();
            ServerLifecycleResult result = await Lifecycle(db, coordinator, audit)
                .DeleteAsync(user, serverId, "survivors", plan: null);

            await Assert.That(result.Failure).IsEqualTo(ServerLifecycleFailure.NotAuthorized);
            await Assert.That(coordinator.LastRequest).IsNull();
            await Assert.That(audit.Entries.Single().Outcome).IsEqualTo(AuditOutcome.Denied);
        });
    }

    [Test]
    public async Task Delete_reports_host_offline_when_the_host_is_not_connected()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerDelete);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerLifecycleResult result = await Lifecycle(db, new RecordingCoordinator { ThrowOffline = true }, new CapturingAuditWriter())
                .DeleteAsync(user, serverId, "survivors", plan: null);

            await Assert.That(result.Failure).IsEqualTo(ServerLifecycleFailure.HostOffline);
        });
    }

    [Test]
    public async Task Delete_reports_server_busy_when_the_per_server_lock_refuses()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerDelete);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerLifecycleResult result = await Lifecycle(db, new RecordingCoordinator { ThrowBusy = true }, new CapturingAuditWriter())
                .DeleteAsync(user, serverId, "survivors", plan: null);

            await Assert.That(result.Failure).IsEqualTo(ServerLifecycleFailure.ServerBusy);
        });
    }

    private static ServerLifecycle Lifecycle(
        ZWardenDbContext db,
        RecordingCoordinator coordinator,
        CapturingAuditWriter audit,
        IHostCapacityCache? capacity = null)
        => new(
            new ServerRepository(db),
            new PermissionChecker(db, new TestTenantContext(Tenant)),
            coordinator,
            audit,
            capacity ?? new HostCapacityCache());

    private sealed class RecordingCoordinator : IOperationCoordinator
    {
        public bool ThrowBusy { get; init; }

        public bool ThrowOffline { get; init; }

        public EnqueueOperationRequest? LastRequest { get; private set; }

        public Task<Operation> EnqueueAsync(
            EnqueueOperationRequest request,
            UserId? actor = null,
            CancellationToken cancellationToken = default)
        {
            if (ThrowBusy)
            {
                throw new ServerBusyException(request.ServerId!.Value);
            }

            if (ThrowOffline)
            {
                throw new HostOfflineException(request.ServerId!.Value);
            }

            LastRequest = request;
            return Task.FromResult(Operation.Enqueue(
                request.AgentId, request.Kind, request.IsMutating, request.IdempotencyKey, Now, request.ServerId));
        }

        public Task<Operation> RequestCancellationAsync(
            OperationId operationId,
            UserId? actor = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static async Task<ServerId> SeedServerAsync(
        DbContextOptions options, AgentId agent, int? gamePort = null, long? heapSizeBytes = null)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
        Server server = Server.Import(agent, ServerId.New(), "survivors", Now);
        if (gamePort is { } port)
        {
            server.RecordContainer($"c-{Guid.NewGuid():N}", port, port + 1, heapSizeBytes);
        }

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
