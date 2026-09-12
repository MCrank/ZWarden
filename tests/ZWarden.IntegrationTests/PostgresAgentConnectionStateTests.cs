using Microsoft.EntityFrameworkCore;
using Npgsql;
using ZWarden.Application.Tenancy;
using ZWarden.Domain.Agents;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Agents;
using ZWarden.Infrastructure.Persistence;
using ZWarden.TestSupport;

namespace ZWarden.IntegrationTests;

/// <summary>
/// F10 on the networked tier (ADR 0016; ADR 0005): the <c>AgentConnectionState</c> migration applies on a
/// real PostgreSQL 18, and an Agent's observed connection state (connect → heartbeat → disconnect) persists
/// and round-trips there too — the same behaviour proven on SQLite offline, no provider branch. Tier-2 /
/// networked.
/// </summary>
[ParallelLimiter<ContainerParallelLimit>]
public class PostgresAgentConnectionStateTests
{
    private static readonly DateTimeOffset ConnectedAt = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset HeartbeatAt = ConnectedAt.AddSeconds(30);
    private static readonly DateTimeOffset DisconnectedAt = ConnectedAt.AddMinutes(5);

    [ClassDataSource<PostgresFixture>(Shared = SharedType.PerAssembly)]
    public required PostgresFixture Postgres { get; init; }

    [Test]
    [Category("Networked")]
    [Timeout(300_000)]
    public async Task Connection_state_migrates_and_round_trips_on_postgres(CancellationToken cancellationToken)
    {
        TenantId tenant = TenantId.New();
        const string agentHash = "connstatehashaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        string connectionString = UniqueDatabase();
        DbContextOptions options = new DbContextOptionsBuilder<ZWardenDbContext>()
            .UseZWardenProvider(ZWardenDbProvider.Postgres, connectionString)
            .Options;
        try
        {
            AgentId id;
            await using (ZWardenDbContext db = new(options, new TestTenantContext(tenant)))
            {
                await db.Database.MigrateAsync(cancellationToken); // includes AgentConnectionState.
                Agent agent = Agent.Enroll(agentHash, EnrollmentId.New(), ConnectedAt);
                id = agent.Id;
                db.Set<Agent>().Add(agent);
                await db.SaveChangesAsync(cancellationToken);
            }

            await Stamp(options, tenant, ConnectedAt, w => w.MarkConnectedAsync(id, 1, cancellationToken));
            await AssertState(options, tenant, id, AgentConnectionState.Connected, ConnectedAt, 1, cancellationToken);

            await Stamp(options, tenant, HeartbeatAt, w => w.MarkHeartbeatAsync(id, cancellationToken));
            await AssertState(options, tenant, id, AgentConnectionState.Connected, HeartbeatAt, 1, cancellationToken);

            await Stamp(options, tenant, DisconnectedAt, w => w.MarkDisconnectedAsync(id, cancellationToken));
            await AssertState(options, tenant, id, AgentConnectionState.Disconnected, DisconnectedAt, 1, cancellationToken);
        }
        finally
        {
            await DropDatabaseAsync(connectionString);
        }
    }

    private static async Task Stamp(
        DbContextOptions options,
        TenantId tenant,
        DateTimeOffset now,
        Func<AgentConnectionStateWriter, Task> act)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(tenant));
        await act(new AgentConnectionStateWriter(db, new AgentRepository(db), new FixedClock(now)));
    }

    private static async Task AssertState(
        DbContextOptions options,
        TenantId tenant,
        AgentId id,
        AgentConnectionState state,
        DateTimeOffset lastSeen,
        int protocolVersion,
        CancellationToken cancellationToken)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(tenant));
        Agent stored = (await new AgentRepository(db).FindByIdAsync(id, cancellationToken))!;
        await Assert.That(stored.ConnectionState).IsEqualTo(state);
        await Assert.That(stored.LastSeenAt).IsEqualTo(lastSeen);
        await Assert.That(stored.LastProtocolVersion).IsEqualTo(protocolVersion);
    }

    private string UniqueDatabase()
    {
        NpgsqlConnectionStringBuilder builder = new(Postgres.ConnectionString)
        {
            Database = $"zw_{Guid.NewGuid():N}",
        };
        return builder.ConnectionString;
    }

    private static async Task DropDatabaseAsync(string connectionString)
    {
        DbContextOptions options = new DbContextOptionsBuilder<ZWardenDbContext>()
            .UseZWardenProvider(ZWardenDbProvider.Postgres, connectionString)
            .Options;
        await using ZWardenDbContext db = new(options, new SingleTenantContext());
        await db.Database.EnsureDeletedAsync();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
