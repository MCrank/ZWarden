using Microsoft.Extensions.Options;
using ZWarden.Domain.Agents;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Agents;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Infrastructure.Tests.Agents;

/// <summary>
/// F10 S4: the connection monitor's sweep (<see cref="AgentConnectionSweeper"/>) reconciles Agents recorded
/// connected but not heard from within <see cref="AgentConnectionMonitorOptions.StaleAfter"/> to disconnected,
/// and audits each — the safety net for a keepalive that has not fired or a Web crash that left a
/// <c>Connected</c> row. It only moves <c>Connected → Disconnected</c>; a fresh connection is left alone and a
/// record is never promoted back to current (trust-boundaries.md §3).
/// </summary>
public class AgentConnectionSweeperTests
{
    private static readonly DateTimeOffset Base = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(90);

    [Test]
    public async Task Sweep_marks_stale_connections_disconnected_and_leaves_fresh_ones()
    {
        await TrustTestHarness.WithSqlite(async options =>
        {
            AgentId stale;
            AgentId fresh;

            await using (ZWardenDbContext ctx = TrustTestHarness.Context(options))
            {
                stale = Seed(ctx);
                fresh = Seed(ctx);
                await ctx.SaveChangesAsync();

                // The stale one was last seen at Base; the fresh one 150s later.
                await Writer(ctx, Base).MarkConnectedAsync(stale, protocolVersion: 1);
                await Writer(ctx, Base.AddSeconds(150)).MarkConnectedAsync(fresh, protocolVersion: 1);
            }

            CapturingAuditWriter audit = new();
            DateTimeOffset now = Base.AddSeconds(200); // threshold = now - 90s = Base + 110s.

            await using (ZWardenDbContext ctx = TrustTestHarness.Context(options))
            {
                int reconciled = await Sweeper(ctx, audit, now).SweepAsync();
                await Assert.That(reconciled).IsEqualTo(1);
            }

            await using (ZWardenDbContext ctx = TrustTestHarness.Context(options))
            {
                AgentRepository repo = new(ctx);
                await Assert.That((await repo.FindByIdAsync(stale))!.ConnectionState)
                    .IsEqualTo(AgentConnectionState.Disconnected);
                await Assert.That((await repo.FindByIdAsync(fresh))!.ConnectionState)
                    .IsEqualTo(AgentConnectionState.Connected);
            }

            await Assert.That(audit.Actions).Contains(AgentConnectionAuditActions.Disconnected);
        });
    }

    [Test]
    public async Task Sweep_is_idempotent_and_never_promotes_a_record_to_current()
    {
        await TrustTestHarness.WithSqlite(async options =>
        {
            AgentId stale;
            await using (ZWardenDbContext ctx = TrustTestHarness.Context(options))
            {
                stale = Seed(ctx);
                await ctx.SaveChangesAsync();
                await Writer(ctx, Base).MarkConnectedAsync(stale, protocolVersion: 1);
            }

            CapturingAuditWriter audit = new();
            DateTimeOffset now = Base.AddSeconds(200);

            await using (ZWardenDbContext ctx = TrustTestHarness.Context(options))
            {
                await Assert.That(await Sweeper(ctx, audit, now).SweepAsync()).IsEqualTo(1);
            }

            await using (ZWardenDbContext ctx = TrustTestHarness.Context(options))
            {
                // Second sweep finds nothing to do — the disconnected record is not re-touched or re-audited.
                await Assert.That(await Sweeper(ctx, audit, now.AddSeconds(60)).SweepAsync()).IsEqualTo(0);
                await Assert.That((await new AgentRepository(ctx).FindByIdAsync(stale))!.ConnectionState)
                    .IsEqualTo(AgentConnectionState.Disconnected);
            }
        });
    }

    private static AgentId Seed(ZWardenDbContext ctx)
    {
        Agent agent = Agent.Enroll(
            TrustTestHarness.Hasher.Hash(TrustTestHarness.Hasher.Generate("zwa")),
            EnrollmentId.New(),
            Base);
        ctx.Add(agent);
        return agent.Id;
    }

    private static AgentConnectionStateWriter Writer(ZWardenDbContext ctx, DateTimeOffset now)
        => new(ctx, new AgentRepository(ctx), new StubClock(now));

    private static AgentConnectionSweeper Sweeper(ZWardenDbContext ctx, CapturingAuditWriter audit, DateTimeOffset now)
        => new(
            ctx,
            new AgentRepository(ctx),
            audit,
            new StubClock(now),
            Options.Create(new AgentConnectionMonitorOptions { StaleAfter = StaleAfter }));
}
