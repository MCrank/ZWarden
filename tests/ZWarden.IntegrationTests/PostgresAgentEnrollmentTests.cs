using Microsoft.EntityFrameworkCore;
using Npgsql;
using ZWarden.Application.Tenancy;
using ZWarden.Domain.Agents;
using ZWarden.Domain.Enrollments;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Agents;
using ZWarden.Infrastructure.Persistence;
using ZWarden.TestSupport;

namespace ZWarden.IntegrationTests;

/// <summary>
/// F9 on the networked tier (ADR 0007; ADR 0016): the <c>AgentEnrollment</c> migration applies on a real
/// PostgreSQL 18, and the <see cref="Enrollment"/>/<see cref="Agent"/> records persist and stay
/// tenant-scoped there too — the same behaviour proven on SQLite offline, no provider branch (ADR 0005).
/// Each test uses its own database on the shared container. Tier-2 / networked.
/// </summary>
[ParallelLimiter<ContainerParallelLimit>]
public class PostgresAgentEnrollmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

    [ClassDataSource<PostgresFixture>(Shared = SharedType.PerAssembly)]
    public required PostgresFixture Postgres { get; init; }

    [Test]
    [Category("Networked")]
    [Timeout(300_000)]
    public async Task The_migration_applies_and_records_persist_on_postgres(CancellationToken cancellationToken)
    {
        TenantId tenantA = TenantId.New();
        const string enrollmentHash = "enrollmenthashaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string agentHash = "agenthashbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        string connectionString = UniqueDatabase();
        DbContextOptions options = new DbContextOptionsBuilder<ZWardenDbContext>()
            .UseZWardenProvider(ZWardenDbProvider.Postgres, connectionString)
            .Options;
        try
        {
            await using (ZWardenDbContext db = new(options, new TestTenantContext(tenantA)))
            {
                await db.Database.MigrateAsync(cancellationToken); // includes AgentEnrollment.
                db.Set<Enrollment>().Add(Enrollment.Issue(enrollmentHash, UserId.New(), Now, Now.AddMinutes(15)));
                db.Set<Agent>().Add(Agent.Enroll(agentHash, EnrollmentId.New(), Now, "host-alpha"));
                await db.SaveChangesAsync(cancellationToken);
            }

            await using (ZWardenDbContext db = new(options, new TestTenantContext(tenantA)))
            {
                Enrollment enrollment = (await new EnrollmentRepository(db).FindBySecretHashAsync(enrollmentHash, cancellationToken))!;
                Agent agent = (await new AgentRepository(db).FindByCredentialHashAsync(agentHash, cancellationToken))!;
                await Assert.That(enrollment.TenantId).IsEqualTo(tenantA);
                await Assert.That(enrollment.Status).IsEqualTo(EnrollmentStatus.Pending);
                await Assert.That(agent.TenantId).IsEqualTo(tenantA);
                await Assert.That(agent.IsTrusted).IsTrue();
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
    public async Task Records_are_tenant_scoped_on_postgres(CancellationToken cancellationToken)
    {
        TenantId tenantA = TenantId.New();
        TenantId tenantB = TenantId.New();
        const string agentHash = "scopedhashccccccccccccccccccccccccccccccccccccccccccccccccccccc";
        string connectionString = UniqueDatabase();
        DbContextOptions options = new DbContextOptionsBuilder<ZWardenDbContext>()
            .UseZWardenProvider(ZWardenDbProvider.Postgres, connectionString)
            .Options;
        try
        {
            await using (ZWardenDbContext asA = new(options, new TestTenantContext(tenantA)))
            {
                await asA.Database.MigrateAsync(cancellationToken);
                asA.Set<Agent>().Add(Agent.Enroll(agentHash, EnrollmentId.New(), Now));
                await asA.SaveChangesAsync(cancellationToken);
            }

            await using (ZWardenDbContext asB = new(options, new TestTenantContext(tenantB)))
            {
                AgentRepository repo = new(asB);
                await Assert.That(await repo.CountAsync(cancellationToken)).IsEqualTo(0);
                await Assert.That(await repo.FindByCredentialHashAsync(agentHash, cancellationToken)).IsNull();
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
