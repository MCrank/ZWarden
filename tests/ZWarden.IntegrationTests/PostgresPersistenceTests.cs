using Microsoft.EntityFrameworkCore;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Persistence;
using ZWarden.TestSupport;

namespace ZWarden.IntegrationTests;

/// <summary>
/// S6: the same persistence behavior as the SQLite tier, proven on a real PostgreSQL 18 container -
/// no provider branch, just a different connection (ADR 0005). Tier-2 / networked. Tests filter by
/// a unique id so they share one EnsureCreated schema without interfering.
/// </summary>
[ParallelLimiter<ContainerParallelLimit>]
public class PostgresPersistenceTests
{
    [ClassDataSource<PostgresFixture>(Shared = SharedType.PerAssembly)]
    public required PostgresFixture Postgres { get; init; }

    private DbContextOptions Options() =>
        new DbContextOptionsBuilder<TestDbContext>()
            .UseZWardenProvider(ZWardenDbProvider.Postgres, Postgres.ConnectionString)
            .Options;

    [Test]
    [Category("Networked")]
    [Timeout(300_000)]
    public async Task Typed_id_key_round_trips_on_postgres(CancellationToken cancellationToken)
    {
        DbContextOptions options = Options();
        ServerId id = ServerId.New();

        await using (TestDbContext ctx = new(options))
        {
            await ctx.Database.EnsureCreatedAsync(cancellationToken);
            ctx.Add(new Widget { Id = id, Name = "pg" });
            await ctx.SaveChangesAsync(cancellationToken);
        }

        await using (TestDbContext ctx = new(options))
        {
            Widget widget = await ctx.Widgets.Where(w => w.Id == id).SingleAsync(cancellationToken);
            await Assert.That(widget.Name).IsEqualTo("pg");
            await Assert.That(widget.Version).IsNotEqualTo(Guid.Empty);
        }
    }

    [Test]
    [Category("Networked")]
    [Timeout(300_000)]
    public async Task A_stale_update_raises_a_concurrency_conflict_on_postgres(CancellationToken cancellationToken)
    {
        DbContextOptions options = Options();
        ServerId id = ServerId.New();

        await using (TestDbContext seed = new(options))
        {
            await seed.Database.EnsureCreatedAsync(cancellationToken);
            seed.Add(new Widget { Id = id, Name = "v1" });
            await seed.SaveChangesAsync(cancellationToken);
        }

        await using TestDbContext first = new(options);
        await using TestDbContext second = new(options);
        Widget fromFirst = await first.Widgets.Where(w => w.Id == id).SingleAsync(cancellationToken);
        Widget fromSecond = await second.Widgets.Where(w => w.Id == id).SingleAsync(cancellationToken);

        fromFirst.Name = "from-first";
        await first.SaveChangesAsync(cancellationToken);

        fromSecond.Name = "from-second";
        await Assert.That(async () => await second.SaveChangesAsync(cancellationToken))
            .Throws<DbUpdateConcurrencyException>();
    }
}
