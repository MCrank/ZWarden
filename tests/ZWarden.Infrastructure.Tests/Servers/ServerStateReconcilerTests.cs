using Microsoft.EntityFrameworkCore;
using ZWarden.Application.Servers;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Servers;
using ZWarden.Infrastructure.Tests.Agents;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Servers;

/// <summary>
/// F14 S4: the snapshot reconciler (trust-boundaries.md §3 — observed, never inferred). It updates the
/// last-reported run-state of Servers the Agent reported, refreshes the discovery cache, and never creates,
/// deletes, or infers a Server. Proven against a real SQLite database.
/// </summary>
public class ServerStateReconcilerTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Reconcile_records_the_reported_state_on_a_matching_server()
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId id = ServerId.New();
            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerRepository repo = new(db);
            repo.Add(Server.Import(agent, id, "alpha", Now));
            await db.SaveChangesAsync();

            ServerDiscoveryCache cache = new();
            ServerStateReconciler reconciler = new(db, repo, cache, new StubClock(Now.AddMinutes(5)));
            await reconciler.ReconcileAsync(agent, [new DiscoveredServer(id, ServerRunState.Running)]);

            Server reloaded = (await repo.FindByIdAsync(id))!;
            await Assert.That(reloaded.LastRunState).IsEqualTo(ServerRunState.Running);
            await Assert.That(reloaded.LastStateReportedAt).IsEqualTo(Now.AddMinutes(5));
        });
    }

    [Test]
    public async Task Reconcile_caches_every_observed_container_including_unregistered_ones()
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId orphan = ServerId.New();
            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerRepository repo = new(db);
            ServerDiscoveryCache cache = new();
            ServerStateReconciler reconciler = new(db, repo, cache, new StubClock(Now));

            await reconciler.ReconcileAsync(agent, [new DiscoveredServer(orphan, ServerRunState.Stopped)]);

            IReadOnlyList<DiscoveredServer> discovered = cache.GetDiscovered(agent);
            await Assert.That(discovered.Count).IsEqualTo(1);
            await Assert.That(discovered[0].ServerId).IsEqualTo(orphan);
        });
    }

    [Test]
    public async Task Reconcile_never_creates_a_server_for_an_unknown_id()
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerRepository repo = new(db);
            ServerStateReconciler reconciler = new(db, repo, new ServerDiscoveryCache(), new StubClock(Now));

            await reconciler.ReconcileAsync(agent, [new DiscoveredServer(ServerId.New(), ServerRunState.Running)]);

            await Assert.That(await repo.CountAsync()).IsEqualTo(0);
        });
    }

    [Test]
    public async Task Reconcile_leaves_a_server_the_agent_did_not_report_untouched()
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId reported = ServerId.New();
            ServerId silent = ServerId.New();
            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerRepository repo = new(db);
            repo.Add(Server.Import(agent, reported, "reported", Now));
            repo.Add(Server.Import(agent, silent, "silent", Now));
            await db.SaveChangesAsync();

            ServerStateReconciler reconciler = new(db, repo, new ServerDiscoveryCache(), new StubClock(Now.AddMinutes(5)));
            await reconciler.ReconcileAsync(agent, [new DiscoveredServer(reported, ServerRunState.Running)]);

            Server untouched = (await repo.FindByIdAsync(silent))!;
            await Assert.That(untouched.LastRunState).IsEqualTo(ServerRunState.Unknown);
            await Assert.That(untouched.LastStateReportedAt).IsNull();
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
