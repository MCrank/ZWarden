using Microsoft.EntityFrameworkCore;

namespace ZWarden.Infrastructure.Persistence;

/// <summary>The supported v1.0 database providers (ADR 0005).</summary>
public enum ZWardenDbProvider
{
    /// <summary>SQLite - the default self-hosted mode. Single writing process only (ADR 0005 condition 4).</summary>
    Sqlite,

    /// <summary>PostgreSQL - larger installations and every hosted deployment.</summary>
    Postgres,
}

/// <summary>Wires the chosen provider onto a <see cref="DbContextOptionsBuilder"/> (Q1).</summary>
public static class ZWardenDbProviderExtensions
{
    /// <summary>Parses a provider name (case-insensitive); throws on an unknown value (fail fast).</summary>
    public static ZWardenDbProvider ParseProvider(string? name)
        => name?.Trim().ToLowerInvariant() switch
        {
            "sqlite" => ZWardenDbProvider.Sqlite,
            "postgres" or "postgresql" or "npgsql" => ZWardenDbProvider.Postgres,
            _ => throw new ArgumentException(
                $"Unknown database provider '{name}'. Use 'sqlite' or 'postgres'.", nameof(name)),
        };

    /// <summary>The default command timeout in seconds (ADR 0005 condition 2 - never 0; F0's value).</summary>
    public const int CommandTimeoutSeconds = 30;

    /// <summary>
    /// Configures the builder for the given provider and connection string, wiring the version
    /// stamper on both providers and the SQLite hardening interceptor + command timeout on SQLite.
    /// </summary>
    public static DbContextOptionsBuilder UseZWardenProvider(
        this DbContextOptionsBuilder builder,
        ZWardenDbProvider provider,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddInterceptors(new VersionStampingInterceptor(), new TenantScopeInterceptor());

        return provider switch
        {
            ZWardenDbProvider.Sqlite => builder
                .UseSqlite(connectionString, o => o.CommandTimeout(CommandTimeoutSeconds))
                .AddInterceptors(new SqliteHardeningInterceptor()),
            ZWardenDbProvider.Postgres => builder
                .UseNpgsql(connectionString, o => o.CommandTimeout(CommandTimeoutSeconds)),
            _ => throw new ArgumentOutOfRangeException(nameof(provider)),
        };
    }
}
