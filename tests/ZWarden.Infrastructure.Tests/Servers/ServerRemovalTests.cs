using Microsoft.EntityFrameworkCore;
using ZWarden.Application.Servers;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Operations;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Servers;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Servers;

/// <summary>
/// #271: a successful <see cref="OperationKind.DeleteServer"/> completion removes the Server row and its server-scoped
/// role assignments (D1), and forgets the Server in the discovery cache so it is not offered for import. The kind is
/// read from the persisted Operation, never the wire, and only the Agent that owns the Server can trigger it
/// (trust-boundaries §3). Proven against a real SQLite database.
/// </summary>
public class ServerRemovalTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task A_succeeded_delete_removes_the_server_its_scoped_grants_and_its_discovery_entry()
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            ServerId sibling = await SeedServerAsync(options, agent);
            UserId user = UserId.New();
            await SeedAssignmentAsync(options, user, serverId);
            await SeedAssignmentAsync(options, user, sibling);
            await SeedAssignmentAsync(options, user, server: null);
            OperationId operationId = await SeedOperationAsync(options, agent, OperationKind.DeleteServer, serverId);
            var discovery = new ServerDiscoveryCache();
            discovery.Record(agent, [new DiscoveredServer(serverId, ServerRunState.Stopped), new DiscoveredServer(sibling, ServerRunState.Running)]);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            bool removed = await Removal(db, discovery).RecordDeletedAsync(operationId, agent);

            await Assert.That(removed).IsTrue();
            await Assert.That(await new ServerRepository(db).FindByIdAsync(serverId)).IsNull();
            await Assert.That(await new ServerRepository(db).FindByIdAsync(sibling)).IsNotNull();
            List<RoleAssignment> left = await db.Set<RoleAssignment>().ToListAsync();
            await Assert.That(left.Count).IsEqualTo(2);
            await Assert.That(left.Any(a => a.ServerId == serverId)).IsFalse();
            await Assert.That(discovery.GetDiscovered(agent).Select(d => d.ServerId)).IsEquivalentTo([sibling]);
        });
    }

    [Test]
    public async Task A_completion_from_an_agent_that_does_not_own_the_server_removes_nothing()
    {
        await WithSqlite(async options =>
        {
            AgentId owner = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, owner);
            OperationId operationId = await SeedOperationAsync(options, owner, OperationKind.DeleteServer, serverId);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            bool removed = await Removal(db, new ServerDiscoveryCache()).RecordDeletedAsync(operationId, AgentId.New());

            await Assert.That(removed).IsFalse();
            await Assert.That(await new ServerRepository(db).FindByIdAsync(serverId)).IsNotNull();
        });
    }

    [Test]
    public async Task Another_kind_of_operation_never_removes_the_server()
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            OperationId operationId = await SeedOperationAsync(options, agent, OperationKind.StopServer, serverId);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            bool removed = await Removal(db, new ServerDiscoveryCache()).RecordDeletedAsync(operationId, agent);

            await Assert.That(removed).IsFalse();
            await Assert.That(await new ServerRepository(db).FindByIdAsync(serverId)).IsNotNull();
        });
    }

    [Test]
    public async Task A_redelivered_completion_for_an_already_removed_server_is_a_no_op()
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            OperationId operationId = await SeedOperationAsync(options, agent, OperationKind.DeleteServer, serverId);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerRemoval sut = Removal(db, new ServerDiscoveryCache());
            await sut.RecordDeletedAsync(operationId, agent);

            await Assert.That(await sut.RecordDeletedAsync(operationId, agent)).IsFalse();
        });
    }

    private static ServerRemoval Removal(ZWardenDbContext db, IServerDiscoveryCache discovery)
        => new(db, new ServerRepository(db), new OperationRepository(db), discovery);

    private static async Task<ServerId> SeedServerAsync(DbContextOptions options, AgentId agent)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
        Server server = Server.Import(agent, ServerId.New(), "survivors", Now);
        new ServerRepository(db).Add(server);
        await db.SaveChangesAsync();
        return server.Id;
    }

    private static async Task<OperationId> SeedOperationAsync(
        DbContextOptions options, AgentId agent, OperationKind kind, ServerId serverId)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
        Operation operation = Operation.Enqueue(agent, kind, isMutating: true, Guid.NewGuid().ToString("N"), Now, serverId);
        new OperationRepository(db).Add(operation);
        await db.SaveChangesAsync();
        return operation.Id;
    }

    private static async Task SeedAssignmentAsync(DbContextOptions options, UserId user, ServerId? server)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
        Role role = Role.CreateCustom(Tenant, $"role-{Guid.NewGuid():N}");
        role.Grant(Permissions.ServerView);
        db.Set<Role>().Add(role);
        db.Set<RoleAssignment>().Add(server is { } id
            ? RoleAssignment.ForServer(Tenant, user, role.Id, id)
            : RoleAssignment.TenantWide(Tenant, user, role.Id));
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
