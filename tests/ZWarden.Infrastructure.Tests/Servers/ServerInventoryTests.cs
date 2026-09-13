using Microsoft.EntityFrameworkCore;
using ZWarden.Application.Operations;
using ZWarden.Application.Servers;
using ZWarden.Domain.Agents;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Enrollments;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Agents;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Servers;
using ZWarden.Infrastructure.Tests.Agents;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Servers;

/// <summary>
/// F14 S4: the inventory + import service, fail-closed (ADR 0018). Import re-checks <c>Server.Register</c>,
/// the Agent's existence, and that the target was actually discovered; reads are authorized per Server.
/// Proven against a real SQLite database, a real <see cref="PermissionChecker"/>, and seeded assignments.
/// </summary>
public class ServerInventoryTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);
    private const string AgentHash = "agenthashaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Test]
    public async Task Import_adopts_a_discovered_orphan_when_authorized_and_is_idempotent()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            await SeedAssignmentAsync(options, user, server: null, Permissions.ServerRegister);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            AgentId agent = await PersistAgentAsync(db);
            ServerId orphan = ServerId.New();
            ServerDiscoveryCache cache = new();
            cache.Record(agent, [new DiscoveredServer(orphan, ServerRunState.Stopped)]);
            CapturingAuditWriter audit = new();
            ServerInventory inventory = Inventory(db, cache, audit);

            ServerImportResult first = await inventory.ImportAsync(user, agent, orphan, "adopted");
            await Assert.That(first.Succeeded).IsTrue();
            await Assert.That(first.Server).IsEqualTo(orphan);
            await Assert.That(audit.Actions).Contains(ServerAuditActions.Imported);

            ServerImportResult second = await inventory.ImportAsync(user, agent, orphan, "adopted-again");
            await Assert.That(second.Succeeded).IsTrue();
            await Assert.That(await new ServerRepository(db).CountAsync()).IsEqualTo(1);
        });
    }

    [Test]
    public async Task Import_denies_without_server_register()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            AgentId agent = await PersistAgentAsync(db);
            ServerId orphan = ServerId.New();
            ServerDiscoveryCache cache = new();
            cache.Record(agent, [new DiscoveredServer(orphan, ServerRunState.Stopped)]);
            ServerInventory inventory = Inventory(db, cache, new CapturingAuditWriter());

            ServerImportResult result = await inventory.ImportAsync(user, agent, orphan, "nope");

            await Assert.That(result.Succeeded).IsFalse();
            await Assert.That(result.Failure).IsEqualTo(ServerImportFailure.NotAuthorized);
        });
    }

    [Test]
    public async Task Import_rejects_an_unknown_agent()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            await SeedAssignmentAsync(options, user, server: null, Permissions.ServerRegister);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerInventory inventory = Inventory(db, new ServerDiscoveryCache(), new CapturingAuditWriter());

            ServerImportResult result = await inventory.ImportAsync(user, AgentId.New(), ServerId.New(), "nope");

            await Assert.That(result.Failure).IsEqualTo(ServerImportFailure.AgentNotFound);
        });
    }

    [Test]
    public async Task Import_rejects_an_id_that_was_not_discovered()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            await SeedAssignmentAsync(options, user, server: null, Permissions.ServerRegister);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            AgentId agent = await PersistAgentAsync(db);
            ServerInventory inventory = Inventory(db, new ServerDiscoveryCache(), new CapturingAuditWriter());

            ServerImportResult result = await inventory.ImportAsync(user, agent, ServerId.New(), "forged");

            await Assert.That(result.Failure).IsEqualTo(ServerImportFailure.NotDiscovered);
        });
    }

    [Test]
    public async Task Register_creates_the_server_and_enqueues_a_provision_operation()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            await SeedAssignmentAsync(options, user, server: null, Permissions.ServerRegister);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            AgentId agent = await PersistAgentAsync(db);
            StubOperationCoordinator coordinator = new();
            CapturingAuditWriter audit = new();
            ServerInventory inventory = Inventory(db, new ServerDiscoveryCache(), audit, coordinator);

            ServerRegisterResult result = await inventory.RegisterAsync(user, agent, "survivors-new");

            await Assert.That(result.Succeeded).IsTrue();
            await Assert.That(result.Server).IsNotNull();
            await Assert.That(result.Operation).IsNotNull();
            await Assert.That(audit.Actions).Contains(ServerAuditActions.Registered);

            // The record exists in Unknown state, on the chosen host.
            Server stored = (await new ServerRepository(db).FindByIdAsync(result.Server!.Value))!;
            await Assert.That(stored.Name).IsEqualTo("survivors-new");
            await Assert.That(stored.AgentId).IsEqualTo(agent);
            await Assert.That(stored.LastRunState).IsEqualTo(ServerRunState.Unknown);

            // A mutating, server-scoped provisioning Operation was enqueued for it.
            await Assert.That(coordinator.LastRequest!.Kind).IsEqualTo(OperationKind.ProvisionServer);
            await Assert.That(coordinator.LastRequest!.IsMutating).IsTrue();
            await Assert.That(coordinator.LastRequest!.ServerId).IsEqualTo(result.Server);
        });
    }

    [Test]
    public async Task Register_denies_without_server_register()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            AgentId agent = await PersistAgentAsync(db);
            ServerInventory inventory = Inventory(db, new ServerDiscoveryCache(), new CapturingAuditWriter());

            ServerRegisterResult result = await inventory.RegisterAsync(user, agent, "nope");

            await Assert.That(result.Failure).IsEqualTo(ServerRegisterFailure.NotAuthorized);
        });
    }

    [Test]
    public async Task Register_rejects_an_unknown_agent()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            await SeedAssignmentAsync(options, user, server: null, Permissions.ServerRegister);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerInventory inventory = Inventory(db, new ServerDiscoveryCache(), new CapturingAuditWriter());

            ServerRegisterResult result = await inventory.RegisterAsync(user, AgentId.New(), "nope");

            await Assert.That(result.Failure).IsEqualTo(ServerRegisterFailure.AgentNotFound);
        });
    }

    [Test]
    public async Task ListVisible_with_a_tenant_wide_view_grant_sees_all_servers()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            await SeedAssignmentAsync(options, user, server: null, Permissions.ServerView);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerRepository repo = new(db);
            AgentId agent = AgentId.New();
            repo.Add(Server.Import(agent, ServerId.New(), "one", Now));
            repo.Add(Server.Import(agent, ServerId.New(), "two", Now));
            await db.SaveChangesAsync();

            ServerInventory inventory = Inventory(db, new ServerDiscoveryCache(), new CapturingAuditWriter());
            IReadOnlyList<ServerSummary> visible = await inventory.ListVisibleAsync(user);

            await Assert.That(visible.Count).IsEqualTo(2);
        });
    }

    [Test]
    public async Task ListVisible_with_a_server_scoped_grant_sees_only_that_server()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId visibleId = ServerId.New();
            ServerId hiddenId = ServerId.New();
            await SeedAssignmentAsync(options, user, visibleId, Permissions.ServerView);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerRepository repo = new(db);
            AgentId agent = AgentId.New();
            repo.Add(Server.Import(agent, visibleId, "visible", Now));
            repo.Add(Server.Import(agent, hiddenId, "hidden", Now));
            await db.SaveChangesAsync();

            ServerInventory inventory = Inventory(db, new ServerDiscoveryCache(), new CapturingAuditWriter());
            IReadOnlyList<ServerSummary> visible = await inventory.ListVisibleAsync(user);

            await Assert.That(visible.Count).IsEqualTo(1);
            await Assert.That(visible[0].Id).IsEqualTo(visibleId);
        });
    }

    [Test]
    public async Task ListVisible_with_no_grant_sees_nothing()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerRepository repo = new(db);
            repo.Add(Server.Import(AgentId.New(), ServerId.New(), "one", Now));
            await db.SaveChangesAsync();

            ServerInventory inventory = Inventory(db, new ServerDiscoveryCache(), new CapturingAuditWriter());
            IReadOnlyList<ServerSummary> visible = await inventory.ListVisibleAsync(user);

            await Assert.That(visible.Count).IsEqualTo(0);
        });
    }

    private static ServerInventory Inventory(
        ZWardenDbContext db,
        ServerDiscoveryCache cache,
        CapturingAuditWriter audit,
        IOperationCoordinator? coordinator = null)
        => new(
            db,
            new ServerRepository(db),
            new AgentRepository(db),
            cache,
            new PermissionChecker(db, new TestTenantContext(Tenant)),
            coordinator ?? new StubOperationCoordinator(),
            audit,
            new StubClock(Now));

    private sealed class StubOperationCoordinator : IOperationCoordinator
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

    private static async Task<AgentId> PersistAgentAsync(ZWardenDbContext db)
    {
        Agent agent = Agent.Enroll(AgentHash, EnrollmentId.New(), Now, "host-alpha");
        db.Set<Agent>().Add(agent);
        await db.SaveChangesAsync();
        return agent.Id;
    }

    private static async Task SeedAssignmentAsync(
        DbContextOptions options,
        UserId user,
        ServerId? server,
        PermissionDefinition permission)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
        Role role = Role.CreateCustom(Tenant, $"role-{Guid.NewGuid():N}");
        role.Grant(permission);
        db.Set<Role>().Add(role);
        db.Set<RoleAssignment>().Add(server is null
            ? RoleAssignment.TenantWide(Tenant, user, role.Id)
            : RoleAssignment.ForServer(Tenant, user, role.Id, server.Value));
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
