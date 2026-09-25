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
    public async Task Reconcile_records_health_from_the_snapshot_when_present()
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId id = ServerId.New();
            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerRepository repo = new(db);
            repo.Add(Server.Import(agent, id, "alpha", Now));
            await db.SaveChangesAsync();

            ServerStateReconciler reconciler = new(db, repo, new ServerDiscoveryCache(), new StubClock(Now.AddMinutes(5)));
            await reconciler.ReconcileAsync(
                agent, [new DiscoveredServer(id, ServerRunState.Running, ServerHealth.Degraded)]);

            Server reloaded = (await repo.FindByIdAsync(id))!;
            await Assert.That(reloaded.LastHealth).IsEqualTo(ServerHealth.Degraded);
            await Assert.That(reloaded.LastHealthReportedAt).IsEqualTo(Now.AddMinutes(5));
        });
    }

    [Test]
    public async Task Reconcile_leaves_health_untouched_when_the_snapshot_omits_it()
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId id = ServerId.New();
            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerRepository repo = new(db);
            repo.Add(Server.Import(agent, id, "alpha", Now));
            await db.SaveChangesAsync();

            ServerStateReconciler reconciler = new(db, repo, new ServerDiscoveryCache(), new StubClock(Now.AddMinutes(5)));
            // Run-state present, health null (a pre-F16 Agent): run-state records, health stays null.
            await reconciler.ReconcileAsync(agent, [new DiscoveredServer(id, ServerRunState.Running)]);

            Server reloaded = (await repo.FindByIdAsync(id))!;
            await Assert.That(reloaded.LastRunState).IsEqualTo(ServerRunState.Running);
            await Assert.That(reloaded.LastHealth).IsNull();
        });
    }

    [Test]
    public async Task RecordObservedHealth_records_health_on_an_owned_server()
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId id = ServerId.New();
            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerRepository repo = new(db);
            repo.Add(Server.Import(agent, id, "alpha", Now));
            await db.SaveChangesAsync();

            ServerStateReconciler reconciler = new(db, repo, new ServerDiscoveryCache(), new StubClock(Now.AddMinutes(2)));
            await reconciler.RecordObservedHealthAsync(agent, id, ServerHealth.Healthy);

            Server reloaded = (await repo.FindByIdAsync(id))!;
            await Assert.That(reloaded.LastHealth).IsEqualTo(ServerHealth.Healthy);
            await Assert.That(reloaded.LastHealthReportedAt).IsEqualTo(Now.AddMinutes(2));
        });
    }

    [Test]
    public async Task RecordObservedState_records_state_on_an_owned_server()
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId id = ServerId.New();
            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerRepository repo = new(db);
            repo.Add(Server.Import(agent, id, "alpha", Now));
            await db.SaveChangesAsync();

            ServerStateReconciler reconciler = new(db, repo, new ServerDiscoveryCache(), new StubClock(Now.AddMinutes(1)));
            await reconciler.RecordObservedStateAsync(agent, id, ServerRunState.Stopping);

            Server reloaded = (await repo.FindByIdAsync(id))!;
            await Assert.That(reloaded.LastRunState).IsEqualTo(ServerRunState.Stopping);
            await Assert.That(reloaded.LastStateReportedAt).IsEqualTo(Now.AddMinutes(1));
        });
    }

    [Test]
    public async Task A_transition_for_a_server_owned_by_another_agent_is_a_no_op()
    {
        await WithSqlite(async options =>
        {
            AgentId owner = AgentId.New();
            AgentId impostor = AgentId.New();
            ServerId id = ServerId.New();
            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerRepository repo = new(db);
            repo.Add(Server.Import(owner, id, "alpha", Now));
            await db.SaveChangesAsync();

            ServerStateReconciler reconciler = new(db, repo, new ServerDiscoveryCache(), new StubClock(Now.AddMinutes(5)));
            // An Agent that does not own the Server may not move its state (trust §8).
            await reconciler.RecordObservedHealthAsync(impostor, id, ServerHealth.Failed);
            await reconciler.RecordObservedStateAsync(impostor, id, ServerRunState.Failed);

            Server reloaded = (await repo.FindByIdAsync(id))!;
            await Assert.That(reloaded.LastHealth).IsNull();
            await Assert.That(reloaded.LastRunState).IsEqualTo(ServerRunState.Unknown);
        });
    }

    [Test]
    public async Task A_health_transition_for_an_unknown_server_is_a_no_op()
    {
        await WithSqlite(async options =>
        {
            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerRepository repo = new(db);
            ServerStateReconciler reconciler = new(db, repo, new ServerDiscoveryCache(), new StubClock(Now));

            await reconciler.RecordObservedHealthAsync(AgentId.New(), ServerId.New(), ServerHealth.Failed);

            await Assert.That(await repo.CountAsync()).IsEqualTo(0);
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

    [Test]
    public async Task RecordProvisioned_records_the_ports_and_container_on_a_matching_server()
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId id = ServerId.New();
            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerRepository repo = new(db);
            // Register mints its own id; use it as the provisioning target.
            Server registered = Server.Register(agent, "alpha", Now);
            ServerId registeredId = registered.Id;
            repo.Add(registered);
            await db.SaveChangesAsync();

            ServerStateReconciler reconciler = new(db, repo, new ServerDiscoveryCache(), new StubClock(Now));
            await reconciler.RecordProvisionedAsync(registeredId, 16265, 16266, "c0ffee");

            Server reloaded = (await repo.FindByIdAsync(registeredId))!;
            await Assert.That(reloaded.GamePort).IsEqualTo(16265);
            await Assert.That(reloaded.QueryPort).IsEqualTo(16266);
            await Assert.That(reloaded.DockerContainerId).IsEqualTo("c0ffee");
        });
    }

    [Test]
    public async Task RecordProvisioned_is_a_no_op_for_an_unknown_server()
    {
        await WithSqlite(async options =>
        {
            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerRepository repo = new(db);
            ServerStateReconciler reconciler = new(db, repo, new ServerDiscoveryCache(), new StubClock(Now));

            // No throw, nothing created.
            await reconciler.RecordProvisionedAsync(ServerId.New(), 16261, 16262, "ghost");

            await Assert.That(await repo.CountAsync()).IsEqualTo(0);
        });
    }

    [Test]
    public async Task RecordInstalledBuild_records_the_build_id_and_time_on_a_matching_server()
    {
        await WithSqlite(async options =>
        {
            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerRepository repo = new(db);
            Server registered = Server.Register(AgentId.New(), "alpha", Now);
            ServerId registeredId = registered.Id;
            repo.Add(registered);
            await db.SaveChangesAsync();

            ServerStateReconciler reconciler = new(db, repo, new ServerDiscoveryCache(), new StubClock(Now));
            await reconciler.RecordInstalledBuildAsync(registeredId, "24909836");

            Server reloaded = (await repo.FindByIdAsync(registeredId))!;
            await Assert.That(reloaded.InstalledBuildId).IsEqualTo("24909836");
            await Assert.That(reloaded.InstalledBuildReportedAt).IsEqualTo(Now);
        });
    }

    [Test]
    public async Task RecordInstalledBuild_is_a_no_op_for_an_unknown_server()
    {
        await WithSqlite(async options =>
        {
            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerRepository repo = new(db);
            ServerStateReconciler reconciler = new(db, repo, new ServerDiscoveryCache(), new StubClock(Now));

            await reconciler.RecordInstalledBuildAsync(ServerId.New(), "24909836");

            await Assert.That(await repo.CountAsync()).IsEqualTo(0);
        });
    }

    [Test]
    public async Task A_reported_build_is_recorded_on_the_owned_server()
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId id = ServerId.New();
            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerRepository repo = new(db);
            repo.Add(Server.Import(agent, id, "alpha", Now));
            await db.SaveChangesAsync();

            ServerStateReconciler reconciler = new(db, repo, new ServerDiscoveryCache(), new StubClock(Now.AddMinutes(2)));
            // #257: the manifest build rides the metrics report, so a first-boot install gets a Version with no Update.
            await reconciler.RecordReportedBuildAsync(agent, id, "24909836");

            Server reloaded = (await repo.FindByIdAsync(id))!;
            await Assert.That(reloaded.InstalledBuildId).IsEqualTo("24909836");
            await Assert.That(reloaded.InstalledBuildReportedAt).IsEqualTo(Now.AddMinutes(2));
        });
    }

    [Test]
    public async Task An_unchanged_reported_build_keeps_the_original_report_time()
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId id = ServerId.New();
            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerRepository repo = new(db);
            Server server = Server.Import(agent, id, "alpha", Now);
            server.RecordObservedBuild("24909836", Now);
            repo.Add(server);
            await db.SaveChangesAsync();

            ServerStateReconciler reconciler = new(db, repo, new ServerDiscoveryCache(), new StubClock(Now.AddHours(1)));
            await reconciler.RecordReportedBuildAsync(agent, id, "24909836");

            Server reloaded = (await repo.FindByIdAsync(id))!;
            await Assert.That(reloaded.InstalledBuildReportedAt).IsEqualTo(Now);
        });
    }

    [Test]
    public async Task A_reported_build_from_another_agent_or_over_length_is_ignored()
    {
        await WithSqlite(async options =>
        {
            AgentId owner = AgentId.New();
            ServerId id = ServerId.New();
            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerRepository repo = new(db);
            repo.Add(Server.Import(owner, id, "alpha", Now));
            await db.SaveChangesAsync();

            ServerStateReconciler reconciler = new(db, repo, new ServerDiscoveryCache(), new StubClock(Now));
            await reconciler.RecordReportedBuildAsync(AgentId.New(), id, "24909836"); // not the owner (trust §8)
            await reconciler.RecordReportedBuildAsync(owner, id, new string('9', 65)); // over the stored bound

            Server reloaded = (await repo.FindByIdAsync(id))!;
            await Assert.That(reloaded.InstalledBuildId).IsNull();
        });
    }

    [Test]
    public async Task A_reported_game_version_is_recorded_on_the_owned_server_only_when_valid()
    {
        await WithSqlite(async options =>
        {
            AgentId owner = AgentId.New();
            ServerId id = ServerId.New();
            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerRepository repo = new(db);
            repo.Add(Server.Import(owner, id, "alpha", Now));
            await db.SaveChangesAsync();

            ServerStateReconciler reconciler = new(db, repo, new ServerDiscoveryCache(), new StubClock(Now));
            // #262: another Agent and an over-length value are both ignored (trust §8, stored bound).
            await reconciler.RecordReportedGameVersionAsync(AgentId.New(), id, "42.20.4");
            await reconciler.RecordReportedGameVersionAsync(owner, id, new string('9', 33));
            await Assert.That((await repo.FindByIdAsync(id))!.GameVersion).IsNull();

            await reconciler.RecordReportedGameVersionAsync(owner, id, "42.20.4");
            await Assert.That((await repo.FindByIdAsync(id))!.GameVersion).IsEqualTo("42.20.4");
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
