using Microsoft.EntityFrameworkCore;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Servers;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Servers;

/// <summary>
/// F14 S2 (PR-A): the <see cref="Server"/> aggregate (<c>srv-</c>) is tenant-owned and read only through the
/// tenant filter (ADR 0016; trust-boundaries §9 rule 4). Its typed ids store as native uuid (ADR 0004) and
/// the last-reported <see cref="ServerRunState"/> stores by name. Proven against a real SQLite database with a
/// two-tenant fixture; the same behaviours run on PostgreSQL on the networked tier.
/// </summary>
public class ServerPersistenceTests
{
    private static readonly TenantId TenantA = TenantId.New();
    private static readonly TenantId TenantB = TenantId.New();
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task A_server_is_stamped_with_the_ambient_tenant_and_scoped_by_it()
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId id = ServerId.New();

            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                ServerRepository repo = new(asA);
                repo.Add(Server.Import(agent, id, "survivors-1", Now));
                await asA.SaveChangesAsync();

                Server? found = await repo.FindByIdAsync(id);
                await Assert.That(found).IsNotNull();
                await Assert.That(found!.TenantId).IsEqualTo(TenantA);
                await Assert.That(found.AgentId).IsEqualTo(agent);
            }

            await using (ZWardenDbContext asB = new(options, new TestTenantContext(TenantB)))
            {
                ServerRepository repo = new(asB);
                await Assert.That(await repo.CountAsync()).IsEqualTo(0);
                await Assert.That(await repo.FindByIdAsync(id)).IsNull();
            }
        });
    }

    [Test]
    public async Task A_server_round_trips_its_observed_state_and_ports()
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId id = ServerId.New();

            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                Server server = Server.Import(agent, id, "survivors-1", Now, "the co-op world");
                server.RecordContainer("c0ffeecafe", gamePort: 16261, queryPort: 16262);
                server.RecordObservedState(ServerRunState.Running, Now.AddMinutes(5));
                asA.Set<Server>().Add(server);
                await asA.SaveChangesAsync();
            }

            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                Server stored = (await new ServerRepository(asA).FindByIdAsync(id))!;
                await Assert.That(stored.Name).IsEqualTo("survivors-1");
                await Assert.That(stored.Description).IsEqualTo("the co-op world");
                await Assert.That(stored.LastRunState).IsEqualTo(ServerRunState.Running);
                await Assert.That(stored.LastStateReportedAt).IsEqualTo(Now.AddMinutes(5));
                await Assert.That(stored.GamePort).IsEqualTo(16261);
                await Assert.That(stored.QueryPort).IsEqualTo(16262);
                await Assert.That(stored.DockerContainerId).IsEqualTo("c0ffeecafe");
            }
        });
    }

    [Test]
    public async Task ListByAgentAsync_returns_only_that_agents_servers()
    {
        await WithSqlite(async options =>
        {
            AgentId agentA = AgentId.New();
            AgentId agentB = AgentId.New();

            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                ServerRepository repo = new(asA);
                repo.Add(Server.Import(agentA, ServerId.New(), "a1", Now));
                repo.Add(Server.Import(agentA, ServerId.New(), "a2", Now));
                repo.Add(Server.Import(agentB, ServerId.New(), "b1", Now));
                await asA.SaveChangesAsync();

                IReadOnlyList<Server> onA = await repo.ListByAgentAsync(agentA);
                await Assert.That(onA.Count).IsEqualTo(2);
                await Assert.That(onA.All(s => s.AgentId == agentA)).IsTrue();
            }
        });
    }

    private static async Task WithSqlite(Func<DbContextOptions, Task> body)
    {
        string file = Path.Combine(Path.GetTempPath(), $"zw-{Guid.NewGuid():N}.db");
        DbContextOptions options = new DbContextOptionsBuilder<ZWardenDbContext>()
            .UseZWardenProvider(ZWardenDbProvider.Sqlite, $"Data Source={file};Pooling=False")
            .Options;
        try
        {
            await using (ZWardenDbContext db = new(options, new TestTenantContext(TenantA)))
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
