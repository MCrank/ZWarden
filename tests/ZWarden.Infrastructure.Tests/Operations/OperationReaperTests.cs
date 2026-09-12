using ZWarden.Application.Operations;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Infrastructure.Operations;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tests.Agents;

namespace ZWarden.Infrastructure.Tests.Operations;

/// <summary>
/// F11 S4: the <see cref="OperationReaper"/> against a real SQLite database — a lease-expired active
/// Operation is failed (releasing its per-server lock), a within-lease one is untouched, and a lease-expired
/// cancellation resolves to failed (ADR 0022).
/// </summary>
public class OperationReaperTests
{
    private static readonly DateTimeOffset Now = OperationTestHarness.Now;
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    private static OperationReaper Reaper(ZWardenDbContext ctx, CapturingAuditWriter audit, DateTimeOffset at)
        => new(ctx, new OperationRepository(ctx), audit, new StubClock(at));

    [Test]
    public async Task Reaps_an_expired_running_lease_to_failed_and_releases_the_lock()
    {
        await OperationTestHarness.WithSqlite(async options =>
        {
            CapturingAuditWriter audit = new();
            ServerId server = ServerId.New();
            OperationId id;
            await using (ZWardenDbContext ctx = OperationTestHarness.Context(options))
            {
                Operation op = await OperationTestHarness.RunningOperation(ctx, Now, Lease, server);
                id = op.Id;
            }

            int reaped;
            await using (ZWardenDbContext ctx = OperationTestHarness.Context(options))
            {
                reaped = await Reaper(ctx, audit, Now + Lease + TimeSpan.FromMinutes(1)).ReapAsync();
            }

            await Assert.That(reaped).IsEqualTo(1);
            await Assert.That(audit.Actions).Contains(OperationAuditActions.Failed);

            await using (ZWardenDbContext verify = OperationTestHarness.Context(options))
            {
                Operation reloaded = (await new OperationRepository(verify).FindByIdAsync(id))!;
                await Assert.That(reloaded.State).IsEqualTo(OperationState.Failed);
                await Assert.That(reloaded.FailureReason).IsNotNull();
                await Assert.That(reloaded.LeaseExpiresAt).IsNull();
            }

            // The per-server slot is free again.
            await using (ZWardenDbContext ctx = OperationTestHarness.Context(options))
            {
                OperationCoordinator coordinator = OperationTestHarness.Coordinator(ctx, audit, new StubClock(Now));
                Operation next = await coordinator.EnqueueAsync(
                    new EnqueueOperationRequest(AgentId.New(), OperationKind.DiagnosticsPing, IsMutating: true, "after-reap", server));
                await Assert.That(next.State).IsEqualTo(OperationState.Pending);
            }
        });
    }

    [Test]
    public async Task Leaves_a_within_lease_operation_untouched()
    {
        await OperationTestHarness.WithSqlite(async options =>
        {
            CapturingAuditWriter audit = new();
            OperationId id;
            await using (ZWardenDbContext ctx = OperationTestHarness.Context(options))
            {
                Operation op = await OperationTestHarness.RunningOperation(ctx, Now, Lease, ServerId.New());
                id = op.Id;
            }

            int reaped;
            await using (ZWardenDbContext ctx = OperationTestHarness.Context(options))
            {
                reaped = await Reaper(ctx, audit, Now + TimeSpan.FromMinutes(1)).ReapAsync();
            }

            await Assert.That(reaped).IsEqualTo(0);
            await using ZWardenDbContext verify = OperationTestHarness.Context(options);
            Operation reloaded = (await new OperationRepository(verify).FindByIdAsync(id))!;
            await Assert.That(reloaded.State).IsEqualTo(OperationState.Running);
        });
    }

    [Test]
    public async Task Reaps_an_expired_cancelling_operation_to_failed()
    {
        await OperationTestHarness.WithSqlite(async options =>
        {
            CapturingAuditWriter audit = new();
            OperationId id;
            await using (ZWardenDbContext ctx = OperationTestHarness.Context(options))
            {
                Operation op = await OperationTestHarness.RunningOperation(ctx, Now, Lease, ServerId.New());
                op.RequestCancel(Now.AddSeconds(10));
                await ctx.SaveChangesAsync();
                id = op.Id;
            }

            await using (ZWardenDbContext ctx = OperationTestHarness.Context(options))
            {
                await Reaper(ctx, audit, Now + Lease + TimeSpan.FromMinutes(1)).ReapAsync();
            }

            await using ZWardenDbContext verify = OperationTestHarness.Context(options);
            Operation reloaded = (await new OperationRepository(verify).FindByIdAsync(id))!;
            await Assert.That(reloaded.State).IsEqualTo(OperationState.Failed);
        });
    }

    [Test]
    public async Task Reaps_nothing_when_no_lease_is_due()
    {
        await OperationTestHarness.WithSqlite(async options =>
        {
            CapturingAuditWriter audit = new();
            await using ZWardenDbContext ctx = OperationTestHarness.Context(options);
            int reaped = await Reaper(ctx, audit, Now + TimeSpan.FromHours(1)).ReapAsync();
            await Assert.That(reaped).IsEqualTo(0);
        });
    }
}
