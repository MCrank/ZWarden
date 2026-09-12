using ZWarden.Domain.Agents;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Agents;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Infrastructure.Tests.Agents;

/// <summary>
/// F10 S3: the Agent's observed connection state (F10, decision 2) is persisted by
/// <see cref="AgentConnectionStateWriter"/> beside its trust state and round-trips — connect stamps
/// <see cref="AgentConnectionState.Connected"/> with the last-seen time and negotiated version; a heartbeat
/// advances last-seen without disturbing the state or trust; disconnect stamps
/// <see cref="AgentConnectionState.Disconnected"/> and keeps the last negotiated version. Proven against a
/// real SQLite database; the same behaviours run on PostgreSQL on the networked tier.
/// </summary>
public class AgentConnectionStatePersistenceTests
{
    private static readonly DateTimeOffset ConnectedAt = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset HeartbeatAt = ConnectedAt.AddSeconds(30);
    private static readonly DateTimeOffset DisconnectedAt = ConnectedAt.AddMinutes(5);

    [Test]
    public async Task Connect_heartbeat_and_disconnect_round_trip_the_agents_observed_state()
    {
        await TrustTestHarness.WithSqlite(async options =>
        {
            const string hash = "connstatehashaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            AgentId id;

            await using (ZWardenDbContext ctx = TrustTestHarness.Context(options))
            {
                Agent agent = Agent.Enroll(hash, EnrollmentId.New(), ConnectedAt);
                id = agent.Id;
                ctx.Set<Agent>().Add(agent);
                await ctx.SaveChangesAsync();

                // A freshly enrolled Agent has never been seen.
                await Assert.That(agent.ConnectionState).IsEqualTo(AgentConnectionState.Disconnected);
                await Assert.That(agent.LastSeenAt).IsNull();
            }

            await using (ZWardenDbContext ctx = TrustTestHarness.Context(options))
            {
                await Writer(ctx, ConnectedAt).MarkConnectedAsync(id, protocolVersion: 1);
            }

            await using (ZWardenDbContext ctx = TrustTestHarness.Context(options))
            {
                Agent stored = (await new AgentRepository(ctx).FindByIdAsync(id))!;
                await Assert.That(stored.ConnectionState).IsEqualTo(AgentConnectionState.Connected);
                await Assert.That(stored.LastSeenAt).IsEqualTo(ConnectedAt);
                await Assert.That(stored.LastProtocolVersion).IsEqualTo(1);
                await Assert.That(stored.IsTrusted).IsTrue();
            }

            await using (ZWardenDbContext ctx = TrustTestHarness.Context(options))
            {
                await Writer(ctx, HeartbeatAt).MarkHeartbeatAsync(id);
            }

            await using (ZWardenDbContext ctx = TrustTestHarness.Context(options))
            {
                Agent stored = (await new AgentRepository(ctx).FindByIdAsync(id))!;
                await Assert.That(stored.LastSeenAt).IsEqualTo(HeartbeatAt);
                await Assert.That(stored.ConnectionState).IsEqualTo(AgentConnectionState.Connected);
                await Assert.That(stored.LastProtocolVersion).IsEqualTo(1);
            }

            await using (ZWardenDbContext ctx = TrustTestHarness.Context(options))
            {
                await Writer(ctx, DisconnectedAt).MarkDisconnectedAsync(id);
            }

            await using (ZWardenDbContext ctx = TrustTestHarness.Context(options))
            {
                Agent stored = (await new AgentRepository(ctx).FindByIdAsync(id))!;
                await Assert.That(stored.ConnectionState).IsEqualTo(AgentConnectionState.Disconnected);
                await Assert.That(stored.LastSeenAt).IsEqualTo(DisconnectedAt);
                // The last negotiated version is kept as the last-known; a drop does not untrust.
                await Assert.That(stored.LastProtocolVersion).IsEqualTo(1);
                await Assert.That(stored.IsTrusted).IsTrue();
            }
        });
    }

    private static AgentConnectionStateWriter Writer(ZWardenDbContext ctx, DateTimeOffset now)
        => new(ctx, new AgentRepository(ctx), new StubClock(now));
}
