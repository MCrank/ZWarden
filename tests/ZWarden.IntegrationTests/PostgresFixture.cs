using Testcontainers.PostgreSql;
using TUnit.Core.Interfaces;

namespace ZWarden.IntegrationTests;

/// <summary>
/// One PostgreSQL container shared across the whole integration assembly (Q5): started
/// once, disposed once. Per-test isolation is the individual test's job (a fresh
/// database/schema or a rolled-back transaction), never a fresh container.
///
/// Testcontainers 4.15.0 marks the parameterless PostgreSqlBuilder() [Obsolete], so
/// under TreatWarningsAsErrors the image MUST go to the constructor - the old
/// new PostgreSqlBuilder().WithImage(...) idiom is a build error (CS0618).
/// </summary>
public sealed class PostgresFixture : IAsyncInitializer, IAsyncDisposable
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("postgres:18-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
