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
    public async Task FindActiveForServer_returns_the_servers_in_flight_mutating_operation()
    {
        // #249: the live header shows STOPPING / RESTARTING from the Server's in-flight lifecycle Operation (a safe
        // stop reports Running for its whole grace window), and disables the lifecycle buttons while it holds the lock.
        await OperationTestHarness.WithSqlite(async options =>
        {
            await using ZWardenDbContext ctx = OperationTestHarness.Context(options);
            ServerId server = ServerId.New();
            Operation restart = Operation.Enqueue(AgentId.New(), OperationKind.RestartServer, isMutating: true, "restart", Now, server);
            ctx.Add(restart);
            await ctx.SaveChangesAsync();

            OperationStore sut = OperationTestHarness.Store(ctx, new CapturingAuditWriter(), new StubClock(Now), Options);
            Operation? active = await sut.FindActiveForServerAsync(server);

            await Assert.That(active?.Id).IsEqualTo(restart.Id);
        });
    }

    private static Operation Finished(
        ServerId server, OperationKind kind, OperationState outcome, DateTimeOffset completedAt, bool mutating = true)
    {
        Operation op = Operation.Enqueue(AgentId.New(), kind, isMutating: mutating, Guid.NewGuid().ToString("N"), completedAt, server);
        op.MarkDispatched(completedAt + Lease, completedAt);
        switch (outcome)
        {
            case OperationState.Succeeded:
                op.Succeed(completedAt);
                break;
            case OperationState.Failed:
                op.Fail("Host port 16261/udp is already published by another container on this host.", completedAt);
                break;
            default:
                op.RequestCancel(completedAt);
                op.Cancel(completedAt);
                break;
        }

        return op;
    }

    [Test]
    public async Task FindUnresolvedFailure_returns_the_servers_latest_mutating_operation_when_it_failed()
    {
        // #266: a failed action's reason must reach the server page — as long as nothing has succeeded since.
        await OperationTestHarness.WithSqlite(async options =>
        {
            await using ZWardenDbContext ctx = OperationTestHarness.Context(options);
            ServerId server = ServerId.New();
            Operation earlier = Finished(server, OperationKind.RestartServer, OperationState.Succeeded, Now);
            await Task.Delay(5); // UUIDv7 ids order by millisecond only (ADR 0004) — space generation.
            Operation failed = Finished(server, OperationKind.RecreateServer, OperationState.Failed, Now.AddMinutes(5));
            ctx.AddRange(earlier, failed);
            await ctx.SaveChangesAsync();

            OperationStore sut = OperationTestHarness.Store(ctx, new CapturingAuditWriter(), new StubClock(Now), Options);
            Operation? failure = await sut.FindUnresolvedFailureForServerAsync(server);

            await Assert.That(failure?.Id).IsEqualTo(failed.Id);
            await Assert.That(failure!.FailureReason).Contains("16261/udp");
        });
    }

    [Test]
    [Arguments(OperationState.Succeeded)]
    [Arguments(OperationState.Cancelled)]
    public async Task FindUnresolvedFailure_is_cleared_by_a_later_finished_operation(OperationState later)
    {
        await OperationTestHarness.WithSqlite(async options =>
        {
            await using ZWardenDbContext ctx = OperationTestHarness.Context(options);
            ServerId server = ServerId.New();
            Operation failed = Finished(server, OperationKind.RecreateServer, OperationState.Failed, Now);
            await Task.Delay(5); // UUIDv7 ids order by millisecond only (ADR 0004) — space generation.
            ctx.AddRange(failed, Finished(server, OperationKind.StartServer, later, Now.AddMinutes(5)));
            await ctx.SaveChangesAsync();

            OperationStore sut = OperationTestHarness.Store(ctx, new CapturingAuditWriter(), new StubClock(Now), Options);

            await Assert.That(await sut.FindUnresolvedFailureForServerAsync(server)).IsNull();
        });
    }

    [Test]
    public async Task FindUnresolvedFailure_ignores_read_only_and_other_servers_failures()
    {
        await OperationTestHarness.WithSqlite(async options =>
        {
            await using ZWardenDbContext ctx = OperationTestHarness.Context(options);
            ServerId server = ServerId.New();
            ctx.AddRange(
                Finished(server, OperationKind.StartServer, OperationState.Succeeded, Now),
                Finished(server, OperationKind.ListPlayers, OperationState.Failed, Now.AddMinutes(1), mutating: false),
                Finished(ServerId.New(), OperationKind.StopServer, OperationState.Failed, Now.AddMinutes(2)));
            await ctx.SaveChangesAsync();

            OperationStore sut = OperationTestHarness.Store(ctx, new CapturingAuditWriter(), new StubClock(Now), Options);

            await Assert.That(await sut.FindUnresolvedFailureForServerAsync(server)).IsNull();
        });
    }

    [Test]
    public async Task FindActiveForServer_ignores_finished_read_only_and_other_servers_operations()
    {
        await OperationTestHarness.WithSqlite(async options =>
        {
            await using ZWardenDbContext ctx = OperationTestHarness.Context(options);
            ServerId server = ServerId.New();
            Operation finished = Operation.Enqueue(AgentId.New(), OperationKind.StopServer, isMutating: true, "done", Now, server);
            finished.MarkDispatched(Now + Lease, Now);
            finished.Succeed(Now);
            Operation readOnly = Operation.Enqueue(AgentId.New(), OperationKind.ListPlayers, isMutating: false, "roster", Now, server);
            Operation elsewhere = Operation.Enqueue(AgentId.New(), OperationKind.StopServer, isMutating: true, "other", Now, ServerId.New());
            ctx.AddRange(finished, readOnly, elsewhere);
            await ctx.SaveChangesAsync();

            OperationStore sut = OperationTestHarness.Store(ctx, new CapturingAuditWriter(), new StubClock(Now), Options);

            await Assert.That(await sut.FindActiveForServerAsync(server)).IsNull();
        });
    }

    [Test]
    public async Task ListActive_returns_every_servers_in_flight_mutating_operation_in_one_read()
    {
        // #253: the fleet board resolves every row's live status from one read, not one query per Server.
        await OperationTestHarness.WithSqlite(async options =>
        {
            await using ZWardenDbContext ctx = OperationTestHarness.Context(options);
            Operation restart = Operation.Enqueue(AgentId.New(), OperationKind.RestartServer, isMutating: true, "restart", Now, ServerId.New());
            Operation stop = Operation.Enqueue(AgentId.New(), OperationKind.StopServer, isMutating: true, "stop", Now, ServerId.New());
            Operation finished = Operation.Enqueue(AgentId.New(), OperationKind.StopServer, isMutating: true, "done", Now, ServerId.New());
            finished.MarkDispatched(Now + Lease, Now);
            finished.Succeed(Now);
            Operation readOnly = Operation.Enqueue(AgentId.New(), OperationKind.ListPlayers, isMutating: false, "roster", Now, ServerId.New());
            ctx.AddRange(restart, stop, finished, readOnly);
            await ctx.SaveChangesAsync();

            OperationStore sut = OperationTestHarness.Store(ctx, new CapturingAuditWriter(), new StubClock(Now), Options);
            IReadOnlyList<Operation> active = await sut.ListActiveAsync();

            await Assert.That(active.Select(o => o.Id)).IsEquivalentTo(new[] { restart.Id, stop.Id });
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
