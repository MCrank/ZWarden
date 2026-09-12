using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ZWarden.Application.Operations;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Infrastructure.Operations;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tests.Agents;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Operations;

/// <summary>Shared fixtures for the F11 operations-engine persistence tests: a real SQLite database, a
/// mutable clock, a capturing audit writer, and a recording dispatcher, wired the way DI will.</summary>
internal static class OperationTestHarness
{
    public static readonly DateTimeOffset Now = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);
    public static readonly TenantId Tenant = TenantId.New();

    public static ZWardenDbContext Context(DbContextOptions options) => new(options, new TestTenantContext(Tenant));

    public static OperationCoordinator Coordinator(
        ZWardenDbContext ctx,
        CapturingAuditWriter audit,
        TimeProvider clock,
        IOperationDispatcher? dispatcher = null)
        => new(ctx, new OperationRepository(ctx), dispatcher ?? new RecordingDispatcher(), audit, clock);

    public static OperationStore Store(
        ZWardenDbContext ctx,
        CapturingAuditWriter audit,
        TimeProvider clock,
        OperationEngineOptions? options = null)
        => new(ctx, new OperationRepository(ctx), audit, clock, Options.Create(options ?? new OperationEngineOptions()));

    /// <summary>Enqueues a mutating operation directly and drives it to Running under a lease, the state the
    /// ingest paths expect (the real dispatcher does this; here we do it by hand so store tests don't need
    /// one).</summary>
    public static async Task<Operation> RunningOperation(
        ZWardenDbContext ctx,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        ServerId? server = null)
    {
        Operation op = Operation.Enqueue(AgentId.New(), OperationKind.DiagnosticsPing, isMutating: server is not null, $"op-{Guid.NewGuid():N}", now, server);
        ctx.Add(op);
        await ctx.SaveChangesAsync();
        op.MarkDispatched(now + leaseDuration, now);
        await ctx.SaveChangesAsync();
        return op;
    }

    public static async Task WithSqlite(Func<DbContextOptions, Task> body)
    {
        string file = Path.Combine(Path.GetTempPath(), $"zw-{Guid.NewGuid():N}.db");
        DbContextOptions options = new DbContextOptionsBuilder<ZWardenDbContext>()
            .UseZWardenProvider(ZWardenDbProvider.Sqlite, $"Data Source={file};Pooling=False")
            .Options;
        try
        {
            await using (ZWardenDbContext db = Context(options))
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

/// <summary>A clock the test can advance, for lease and progress timing.</summary>
internal sealed class MutableClock(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>Records which operations the coordinator handed to dispatch, and reports whether the Agent was
/// reachable (primed via <paramref name="dispatched"/>). Does not itself transition the operation — that is
/// the real Web dispatcher's job (PR-B).</summary>
internal sealed class RecordingDispatcher(bool dispatched = false) : IOperationDispatcher
{
    public List<OperationId> Dispatched { get; } = [];

    public Task<bool> TryDispatchAsync(Operation operation, CancellationToken cancellationToken = default)
    {
        Dispatched.Add(operation.Id);
        return Task.FromResult(dispatched);
    }
}
