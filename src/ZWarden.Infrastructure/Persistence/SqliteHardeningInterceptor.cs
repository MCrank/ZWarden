using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ZWarden.Infrastructure.Persistence;

/// <summary>
/// Applies the SQLite operating conditions (ADR 0005 condition 1) on every connection as it opens -
/// none is a default, and getting WAL wrong is ~10x slower under write contention. Registered only
/// for the SQLite provider. CommandTimeout (condition 2) is set on the provider options, not here.
/// </summary>
public sealed class SqliteHardeningInterceptor : DbConnectionInterceptor
{
    private const string Pragmas =
        "PRAGMA journal_mode=WAL;" +
        "PRAGMA busy_timeout=5000;" +
        "PRAGMA foreign_keys=ON;" +
        "PRAGMA synchronous=NORMAL;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(connection);
        using DbCommand command = connection.CreateCommand();
        command.CommandText = Pragmas;
        command.ExecuteNonQuery();
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = Pragmas;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken).ConfigureAwait(false);
    }
}
