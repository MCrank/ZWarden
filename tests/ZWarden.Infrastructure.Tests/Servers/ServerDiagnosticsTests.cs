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
/// F18: the diagnostics service, fail-closed (ADR 0018). The RCON health check authorizes the server-scoped
/// <c>Server.Diagnostics</c> permission against the specific Server, resolves the Server through the tenant
/// filter (foreign/unknown ⇒ ServerNotFound), audits, and enqueues a <b>non-mutating</b> RconHealthProbe on the
/// Server's Agent — so it never takes the per-server lock. Proven against a real SQLite database, a real
/// <see cref="PermissionChecker"/>, and seeded assignments.
/// </summary>
public class ServerDiagnosticsTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task CheckRconHealth_enqueues_a_non_mutating_probe_on_the_servers_agent_and_audits_it()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerDiagnostics);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            CapturingAuditWriter audit = new();
            ServerDiagnostics sut = Diagnostics(db, coordinator, audit);

            ServerLifecycleResult result = await sut.CheckRconHealthAsync(user, serverId);

            await Assert.That(result.Succeeded).IsTrue();
            await Assert.That(result.Operation).IsNotNull();
            await Assert.That(coordinator.LastRequest!.Kind).IsEqualTo(OperationKind.RconHealthProbe);
            await Assert.That(coordinator.LastRequest!.IsMutating).IsFalse(); // never takes the per-server lock
            await Assert.That(coordinator.LastRequest!.ServerId).IsEqualTo(serverId); // still names the Server
            await Assert.That(coordinator.LastRequest!.AgentId).IsEqualTo(agent);
            await Assert.That(audit.Actions).Contains(ServerAuditActions.RconChecked);
        });
    }

    [Test]
    public async Task CheckRconHealth_denies_without_the_server_scoped_diagnostics_permission()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            // A diagnostics grant on a *different* server does not authorize this one.
            await SeedAssignmentAsync(options, user, ServerId.New(), Permissions.ServerDiagnostics);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerDiagnostics sut = Diagnostics(db, coordinator, new CapturingAuditWriter());

            ServerLifecycleResult result = await sut.CheckRconHealthAsync(user, serverId);

            await Assert.That(result.Succeeded).IsFalse();
            await Assert.That(result.Failure).IsEqualTo(ServerLifecycleFailure.NotAuthorized);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    public async Task CheckRconHealth_reports_server_not_found_for_an_unknown_server()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId unknown = ServerId.New();
            await SeedAssignmentAsync(options, user, unknown, Permissions.ServerDiagnostics);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerDiagnostics sut = Diagnostics(db, new RecordingCoordinator(), new CapturingAuditWriter());

            ServerLifecycleResult result = await sut.CheckRconHealthAsync(user, unknown);

            await Assert.That(result.Failure).IsEqualTo(ServerLifecycleFailure.ServerNotFound);
        });
    }

    private static ServerDiagnostics Diagnostics(
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
        public EnqueueOperationRequest? LastRequest { get; private set; }

        public Task<Operation> EnqueueAsync(
            EnqueueOperationRequest request,
            UserId? actor = null,
            CancellationToken cancellationToken = default)
        {
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
