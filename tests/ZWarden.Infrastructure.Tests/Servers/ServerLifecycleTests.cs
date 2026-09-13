using Microsoft.EntityFrameworkCore;
using ZWarden.Application.Operations;
using ZWarden.Application.Servers;
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

    private static ServerLifecycle Lifecycle(
        ZWardenDbContext db,
        RecordingCoordinator coordinator,
        CapturingAuditWriter audit)
        => new(
            new ServerRepository(db),
            new PermissionChecker(db, new TestTenantContext(Tenant)),
            coordinator,
            audit);

    private sealed class RecordingCoordinator : IOperationCoordinator
    {
        public bool ThrowBusy { get; init; }

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

            LastRequest = request;
            return Task.FromResult(Operation.Enqueue(
                request.AgentId, request.Kind, request.IsMutating, request.IdempotencyKey, Now, request.ServerId));
        }

        public Task<Operation> RequestCancellationAsync(
            OperationId operationId,
            UserId? actor = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
