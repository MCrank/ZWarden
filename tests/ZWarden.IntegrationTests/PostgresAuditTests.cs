using Microsoft.EntityFrameworkCore;
using Npgsql;
using ZWarden.Application.Audit;
using ZWarden.Application.Tenancy;
using ZWarden.Domain.Audit;
using ZWarden.Infrastructure.Audit;
using ZWarden.Infrastructure.Persistence;
using ZWarden.TestSupport;

namespace ZWarden.IntegrationTests;

/// <summary>
/// F6 S6 on the networked tier (ADR 0019): the per-provider <c>Audit</c> migration applies on a real
/// PostgreSQL, and an <see cref="AuditEvent"/> round-trips and orders newest-first there — proving the
/// UTC-DateTime timestamp conversion (which SQLite needs) is also correct on Postgres. Tier-2 / networked.
/// </summary>
[ParallelLimiter<ContainerParallelLimit>]
public class PostgresAuditTests
{
    [ClassDataSource<PostgresFixture>(Shared = SharedType.PerAssembly)]
    public required PostgresFixture Postgres { get; init; }

    [Test]
    [Category("Networked")]
    [Timeout(300_000)]
    public async Task Audit_migration_applies_and_events_round_trip_newest_first_on_postgres(CancellationToken cancellationToken)
    {
        string connectionString = UniqueDatabase();
        DbContextOptions options = new DbContextOptionsBuilder<ZWardenDbContext>()
            .UseZWardenProvider(ZWardenDbProvider.Postgres, connectionString)
            .Options;
        DateTimeOffset t0 = new(2026, 9, 12, 8, 0, 0, TimeSpan.Zero);
        try
        {
            await using (ZWardenDbContext db = new(options, new SingleTenantContext()))
            {
                await db.Database.MigrateAsync(cancellationToken);   // applies every migration incl. Audit
                db.Set<AuditEvent>().Add(AuditEvent.Create("Role.Created", AuditOutcome.Succeeded, t0));
                db.Set<AuditEvent>().Add(AuditEvent.Create("Server.Started", AuditOutcome.Succeeded, t0.AddMinutes(1)));
                await db.SaveChangesAsync(cancellationToken);
            }

            await using (ZWardenDbContext db = new(options, new SingleTenantContext()))
            {
                AuditQueryService service = new(new AuditEventRepository(db));
                IReadOnlyList<AuditEventView> events = await service.QueryAsync(new AuditQuery(), cancellationToken);

                await Assert.That(events.Count).IsEqualTo(2);
                await Assert.That(events[0].Action).IsEqualTo("Server.Started");     // newest first
                await Assert.That(events[0].OccurredAt).IsEqualTo(t0.AddMinutes(1));
                await Assert.That(events[1].OccurredAt).IsEqualTo(t0);
            }
        }
        finally
        {
            await DropDatabaseAsync(connectionString);
        }
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
}
