using Npgsql;

namespace ZWarden.IntegrationTests;

/// <summary>
/// Proves the tier-2 harness: Testcontainers 4.15.0 + Npgsql 10.0.3 compose with TUnit,
/// a real PostgreSQL 18 container answers a query, and the shared-fixture + parallel-cap
/// shape works. Real persistence tests (EF Core, both providers) arrive with Feature 2.
/// </summary>
[ParallelLimiter<ContainerParallelLimit>]
public class PostgresConnectivityTests
{
    [ClassDataSource<PostgresFixture>(Shared = SharedType.PerAssembly)]
    public required PostgresFixture Postgres { get; init; }

    [Test]
    [Category("Networked")]
    [Timeout(300_000)]
    public async Task Shared_container_answers_a_query(CancellationToken cancellationToken)
    {
        await using var source = NpgsqlDataSource.Create(Postgres.ConnectionString);
        await using var command = source.CreateCommand("select version(), gen_random_uuid()");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        await Assert.That(await reader.ReadAsync(cancellationToken)).IsTrue();

        string version = reader.GetString(0);
        Guid uuid = reader.GetGuid(1);

        await Assert.That(version).Contains("PostgreSQL 18");
        await Assert.That(uuid).IsNotEqualTo(Guid.Empty);
    }
}
