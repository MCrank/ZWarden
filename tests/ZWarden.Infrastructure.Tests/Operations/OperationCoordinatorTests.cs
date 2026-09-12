using ZWarden.Application.Operations;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Infrastructure.Operations;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tests.Agents;

namespace ZWarden.Infrastructure.Tests.Operations;

/// <summary>
/// F11 S3: the <see cref="OperationCoordinator"/> against a real SQLite database — enqueue idempotency
/// (PRD 20), the per-server lock realized as a partial unique index (PRD 21, ADR 0005/0022), and
/// cancellation. The Postgres parity for the lock lives in the networked tier.
/// </summary>
public class OperationCoordinatorTests
{
    private static readonly DateTimeOffset Now = OperationTestHarness.Now;

    private static EnqueueOperationRequest Ping(AgentId? agent = null, string? key = null)
        => new(agent ?? AgentId.New(), OperationKind.DiagnosticsPing, IsMutating: false, key ?? $"k-{Guid.NewGuid():N}");

    private static EnqueueOperationRequest Mutating(ServerId server, string? key = null)
        => new(AgentId.New(), OperationKind.DiagnosticsPing, IsMutating: true, key ?? $"k-{Guid.NewGuid():N}", server);

    [Test]
    public async Task Enqueue_persists_a_pending_operation_and_dispatches()
    {
        await OperationTestHarness.WithSqlite(async options =>
        {
            CapturingAuditWriter audit = new();
            RecordingDispatcher dispatcher = new();
            await using ZWardenDbContext ctx = OperationTestHarness.Context(options);
            OperationCoordinator sut = OperationTestHarness.Coordinator(ctx, audit, new StubClock(Now), dispatcher);

            Operation op = await sut.EnqueueAsync(Ping());

            await Assert.That(op.State).IsEqualTo(OperationState.Pending);
            await Assert.That(op.EnqueuedAt).IsEqualTo(Now);
            await Assert.That(audit.Actions).Contains(OperationAuditActions.Enqueued);
            await Assert.That(dispatcher.Dispatched).Contains(op.Id);
        });
    }

    [Test]
    public async Task Enqueue_is_idempotent_by_key()
    {
        await OperationTestHarness.WithSqlite(async options =>
        {
            CapturingAuditWriter audit = new();
            await using ZWardenDbContext ctx = OperationTestHarness.Context(options);
            OperationCoordinator sut = OperationTestHarness.Coordinator(ctx, audit, new StubClock(Now));

            EnqueueOperationRequest request = Ping(key: "same-key");
            Operation first = await sut.EnqueueAsync(request);
            Operation second = await sut.EnqueueAsync(request);

            await Assert.That(second.Id).IsEqualTo(first.Id);
            await using ZWardenDbContext verify = OperationTestHarness.Context(options);
            await Assert.That(await new OperationRepository(verify).CountAsync()).IsEqualTo(1);
        });
    }

    [Test]
    public async Task A_second_mutating_operation_for_the_same_server_is_server_busy()
    {
        await OperationTestHarness.WithSqlite(async options =>
        {
            CapturingAuditWriter audit = new();
            await using ZWardenDbContext ctx = OperationTestHarness.Context(options);
            OperationCoordinator sut = OperationTestHarness.Coordinator(ctx, audit, new StubClock(Now));
            ServerId server = ServerId.New();

            await sut.EnqueueAsync(Mutating(server));

            ServerBusyException? caught = null;
            try
            {
                await sut.EnqueueAsync(Mutating(server));
            }
            catch (ServerBusyException ex)
            {
                caught = ex;
            }

            await Assert.That(caught).IsNotNull();
            await Assert.That(caught!.ServerId).IsEqualTo(server);
        });
    }

    [Test]
    public async Task Mutating_operations_for_different_servers_both_acquire()
    {
        await OperationTestHarness.WithSqlite(async options =>
        {
            CapturingAuditWriter audit = new();
            await using ZWardenDbContext ctx = OperationTestHarness.Context(options);
            OperationCoordinator sut = OperationTestHarness.Coordinator(ctx, audit, new StubClock(Now));

            Operation a = await sut.EnqueueAsync(Mutating(ServerId.New()));
            Operation b = await sut.EnqueueAsync(Mutating(ServerId.New()));

            await Assert.That(a.State).IsEqualTo(OperationState.Pending);
            await Assert.That(b.State).IsEqualTo(OperationState.Pending);
        });
    }

    [Test]
    public async Task A_non_mutating_operation_does_not_block_the_server()
    {
        await OperationTestHarness.WithSqlite(async options =>
        {
            CapturingAuditWriter audit = new();
            await using ZWardenDbContext ctx = OperationTestHarness.Context(options);
            OperationCoordinator sut = OperationTestHarness.Coordinator(ctx, audit, new StubClock(Now));
            ServerId server = ServerId.New();

            await sut.EnqueueAsync(Mutating(server));
            // A read-only operation names the same server but sits outside the lock index.
            Operation readOnly = await sut.EnqueueAsync(
                new EnqueueOperationRequest(AgentId.New(), OperationKind.DiagnosticsPing, IsMutating: false, $"k-{Guid.NewGuid():N}", server));

            await Assert.That(readOnly.State).IsEqualTo(OperationState.Pending);
        });
    }

    [Test]
    public async Task Cancelling_a_pending_mutating_operation_releases_the_lock()
    {
        await OperationTestHarness.WithSqlite(async options =>
        {
            CapturingAuditWriter audit = new();
            await using ZWardenDbContext ctx = OperationTestHarness.Context(options);
            OperationCoordinator sut = OperationTestHarness.Coordinator(ctx, audit, new StubClock(Now));
            ServerId server = ServerId.New();

            Operation first = await sut.EnqueueAsync(Mutating(server));
            Operation cancelled = await sut.RequestCancellationAsync(first.Id);
            await Assert.That(cancelled.State).IsEqualTo(OperationState.Cancelled);
            await Assert.That(audit.Actions).Contains(OperationAuditActions.Cancelled);

            // The slot is free: a new mutating operation for the same server acquires.
            Operation second = await sut.EnqueueAsync(Mutating(server));
            await Assert.That(second.State).IsEqualTo(OperationState.Pending);
        });
    }

    [Test]
    public async Task RequestCancellation_on_an_unknown_operation_throws_not_found()
    {
        await OperationTestHarness.WithSqlite(async options =>
        {
            CapturingAuditWriter audit = new();
            await using ZWardenDbContext ctx = OperationTestHarness.Context(options);
            OperationCoordinator sut = OperationTestHarness.Coordinator(ctx, audit, new StubClock(Now));

            await Assert.That(async () => await sut.RequestCancellationAsync(OperationId.New()))
                .Throws<OperationNotFoundException>();
        });
    }
}
