using Microsoft.EntityFrameworkCore;
using Npgsql;
using ZWarden.Application.Audit;
using ZWarden.Application.Operations;
using ZWarden.Application.Tenancy;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Infrastructure.Operations;
using ZWarden.Infrastructure.Persistence;
using ZWarden.TestSupport;

namespace ZWarden.IntegrationTests;

/// <summary>
/// F11 on the networked tier (ADR 0005/0022): the <c>AddOperations</c> migration applies on a real
/// PostgreSQL 18, the per-server lock (the partial unique index) refuses a second active mutating Operation
/// exactly as on SQLite — proving the "one provider switch, in one place" translates
/// <c>PostgresException 23505</c> to <see cref="ServerBusyException"/> — and the optimistic
/// <c>Version</c> token raises a concurrency conflict on a stale transition. Same behaviour proven on SQLite
/// offline, no provider branch in the test body. Tier-2 / networked.
/// </summary>
[ParallelLimiter<ContainerParallelLimit>]
public class PostgresOperationLockTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

    [ClassDataSource<PostgresFixture>(Shared = SharedType.PerAssembly)]
    public required PostgresFixture Postgres { get; init; }

    [Test]
    [Category("Networked")]
    [Timeout(300_000)]
    public async Task The_per_server_lock_refuses_a_second_mutating_operation_on_postgres(CancellationToken cancellationToken)
    {
        TenantId tenant = TenantId.New();
        string connectionString = UniqueDatabase();
        DbContextOptions options = new DbContextOptionsBuilder<ZWardenDbContext>()
            .UseZWardenProvider(ZWardenDbProvider.Postgres, connectionString)
            .Options;
        try
        {
            ServerId server = ServerId.New();

            await using (ZWardenDbContext db = new(options, new TestTenantContext(tenant)))
            {
                await db.Database.MigrateAsync(cancellationToken); // includes AddOperations.
                OperationCoordinator coordinator = Coordinator(db);

                await coordinator.EnqueueAsync(Mutating(server, "k1"), cancellationToken: cancellationToken);

                ServerBusyException? caught = null;
                try
                {
                    await coordinator.EnqueueAsync(Mutating(server, "k2"), cancellationToken: cancellationToken);
                }
                catch (ServerBusyException ex)
                {
                    caught = ex;
                }

                await Assert.That(caught).IsNotNull();
                await Assert.That(caught!.ServerId).IsEqualTo(server);
            }

            // A different server is unaffected.
            await using (ZWardenDbContext db = new(options, new TestTenantContext(tenant)))
            {
                Operation other = await Coordinator(db).EnqueueAsync(Mutating(ServerId.New(), "k3"), cancellationToken: cancellationToken);
                await Assert.That(other.State).IsEqualTo(OperationState.Pending);
            }
        }
        finally
        {
            await DropDatabaseAsync(connectionString);
        }
    }

    [Test]
    [Category("Networked")]
    [Timeout(300_000)]
    public async Task A_stale_operation_transition_raises_a_concurrency_conflict_on_postgres(CancellationToken cancellationToken)
    {
        TenantId tenant = TenantId.New();
        string connectionString = UniqueDatabase();
        DbContextOptions options = new DbContextOptionsBuilder<ZWardenDbContext>()
            .UseZWardenProvider(ZWardenDbProvider.Postgres, connectionString)
            .Options;
        try
        {
            OperationId id;
            await using (ZWardenDbContext seed = new(options, new TestTenantContext(tenant)))
            {
                await seed.Database.MigrateAsync(cancellationToken);
                Operation op = Operation.Enqueue(AgentId.New(), OperationKind.DiagnosticsPing, isMutating: true, "seed", Now, ServerId.New());
                op.MarkDispatched(Now.AddMinutes(5), Now);
                id = op.Id;
                seed.Add(op);
                await seed.SaveChangesAsync(cancellationToken);
            }

            await using ZWardenDbContext first = new(options, new TestTenantContext(tenant));
            await using ZWardenDbContext second = new(options, new TestTenantContext(tenant));
            Operation fromFirst = (await new OperationRepository(first).FindByIdAsync(id, cancellationToken))!;
            Operation fromSecond = (await new OperationRepository(second).FindByIdAsync(id, cancellationToken))!;

            fromFirst.Succeed(Now.AddMinutes(1));
            await first.SaveChangesAsync(cancellationToken);

            fromSecond.Fail("stale", Now.AddMinutes(2));
            await Assert.That(async () => await second.SaveChangesAsync(cancellationToken))
                .Throws<DbUpdateConcurrencyException>();
        }
        finally
        {
            await DropDatabaseAsync(connectionString);
        }
    }

    private static OperationCoordinator Coordinator(ZWardenDbContext db)
        => new(db, new OperationRepository(db), new NoOpDispatcher(), new NoOpAuditWriter(), new FixedClock(Now));

    private static EnqueueOperationRequest Mutating(ServerId server, string key)
        => new(AgentId.New(), OperationKind.DiagnosticsPing, IsMutating: true, key, server);

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

    private sealed class NoOpDispatcher : IOperationDispatcher
    {
        public Task<bool> TryDispatchAsync(Operation operation, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }

    private sealed class NoOpAuditWriter : IAuditWriter
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
