using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Infrastructure.Operations;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tests.Agents;

namespace ZWarden.Infrastructure.Tests.Operations;

/// <summary>
/// F11 S3: the <see cref="OperationStore"/> ingest of Agent-reported events against a real SQLite database.
/// Ingest is idempotent and defensive (PRD 20) and Agent text is untrusted (trust-boundaries.md §3).
/// </summary>
public class OperationStoreTests
{
    private static readonly DateTimeOffset Now = OperationTestHarness.Now;
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);
    private static readonly OperationEngineOptions Options = new() { LeaseDuration = Lease };

    [Test]
    public async Task ApplyProgress_advances_percentage_and_extends_the_lease()
    {
        await OperationTestHarness.WithSqlite(async options =>
        {
            CapturingAuditWriter audit = new();
            MutableClock clock = new(Now);
            await using ZWardenDbContext ctx = OperationTestHarness.Context(options);
            Operation op = await OperationTestHarness.RunningOperation(ctx, Now, Lease, ServerId.New());

            clock.Advance(TimeSpan.FromMinutes(1));
            OperationStore sut = OperationTestHarness.Store(ctx, audit, clock, Options);
            await sut.ApplyProgressAsync(op.Id, 42, "installing", CancellationToken.None);

            await using ZWardenDbContext verify = OperationTestHarness.Context(options);
            Operation reloaded = (await new OperationRepository(verify).FindByIdAsync(op.Id))!;
            await Assert.That(reloaded.PercentComplete).IsEqualTo(42);
            await Assert.That(reloaded.StatusLine).IsEqualTo("installing");
            await Assert.That(reloaded.LeaseExpiresAt).IsEqualTo(Now.AddMinutes(1) + Lease);
        });
    }

    [Test]
    public async Task CompleteSucceeded_drives_to_succeeded_and_audits()
    {
        await OperationTestHarness.WithSqlite(async options =>
        {
            CapturingAuditWriter audit = new();
            await using ZWardenDbContext ctx = OperationTestHarness.Context(options);
            Operation op = await OperationTestHarness.RunningOperation(ctx, Now, Lease, ServerId.New());

            OperationStore sut = OperationTestHarness.Store(ctx, audit, new StubClock(Now.AddMinutes(1)), Options);
            await sut.CompleteSucceededAsync(op.Id);

            await using ZWardenDbContext verify = OperationTestHarness.Context(options);
            Operation reloaded = (await new OperationRepository(verify).FindByIdAsync(op.Id))!;
            await Assert.That(reloaded.State).IsEqualTo(OperationState.Succeeded);
            await Assert.That(reloaded.PercentComplete).IsEqualTo(100);
            await Assert.That(audit.Actions).Contains(OperationAuditActions.Succeeded);
        });
    }

    [Test]
    public async Task CompleteFailed_while_running_drives_to_failed_with_reason()
    {
        await OperationTestHarness.WithSqlite(async options =>
        {
            CapturingAuditWriter audit = new();
            await using ZWardenDbContext ctx = OperationTestHarness.Context(options);
            Operation op = await OperationTestHarness.RunningOperation(ctx, Now, Lease, ServerId.New());

            OperationStore sut = OperationTestHarness.Store(ctx, audit, new StubClock(Now.AddMinutes(1)), Options);
            await sut.CompleteFailedAsync(op.Id, "steamcmd exited 1");

            await using ZWardenDbContext verify = OperationTestHarness.Context(options);
            Operation reloaded = (await new OperationRepository(verify).FindByIdAsync(op.Id))!;
            await Assert.That(reloaded.State).IsEqualTo(OperationState.Failed);
            await Assert.That(reloaded.FailureReason).IsEqualTo("steamcmd exited 1");
            await Assert.That(audit.Actions).Contains(OperationAuditActions.Failed);
        });
    }

    [Test]
    public async Task CompleteFailed_while_cancelling_records_a_cancellation()
    {
        await OperationTestHarness.WithSqlite(async options =>
        {
            CapturingAuditWriter audit = new();
            await using ZWardenDbContext ctx = OperationTestHarness.Context(options);
            Operation op = await OperationTestHarness.RunningOperation(ctx, Now, Lease, ServerId.New());
            op.RequestCancel(Now.AddSeconds(10));
            await ctx.SaveChangesAsync();

            OperationStore sut = OperationTestHarness.Store(ctx, audit, new StubClock(Now.AddMinutes(1)), Options);
            await sut.CompleteFailedAsync(op.Id, "aborted");

            await using ZWardenDbContext verify = OperationTestHarness.Context(options);
            Operation reloaded = (await new OperationRepository(verify).FindByIdAsync(op.Id))!;
            await Assert.That(reloaded.State).IsEqualTo(OperationState.Cancelled);
            await Assert.That(audit.Actions).Contains(OperationAuditActions.Cancelled);
        });
    }

    [Test]
    public async Task A_redelivered_completion_is_a_no_op()
    {
        await OperationTestHarness.WithSqlite(async options =>
        {
            CapturingAuditWriter audit = new();
            await using ZWardenDbContext ctx = OperationTestHarness.Context(options);
            Operation op = await OperationTestHarness.RunningOperation(ctx, Now, Lease, ServerId.New());

            OperationStore sut = OperationTestHarness.Store(ctx, audit, new StubClock(Now.AddMinutes(1)), Options);
            await sut.CompleteSucceededAsync(op.Id);
            await sut.CompleteFailedAsync(op.Id, "late failure"); // redelivered/contradictory — ignored

            await using ZWardenDbContext verify = OperationTestHarness.Context(options);
            Operation reloaded = (await new OperationRepository(verify).FindByIdAsync(op.Id))!;
            await Assert.That(reloaded.State).IsEqualTo(OperationState.Succeeded);
            await Assert.That(audit.Actions.Count(a => a == OperationAuditActions.Succeeded)).IsEqualTo(1);
        });
    }

    [Test]
    public async Task Ingest_for_an_unknown_operation_is_a_no_op()
    {
        await OperationTestHarness.WithSqlite(async options =>
        {
            CapturingAuditWriter audit = new();
            await using ZWardenDbContext ctx = OperationTestHarness.Context(options);
            OperationStore sut = OperationTestHarness.Store(ctx, audit, new StubClock(Now), Options);

            await sut.ApplyProgressAsync(OperationId.New(), 10, "x", CancellationToken.None);
            await sut.CompleteSucceededAsync(OperationId.New());

            await Assert.That(audit.Entries).IsEmpty();
        });
    }
}
