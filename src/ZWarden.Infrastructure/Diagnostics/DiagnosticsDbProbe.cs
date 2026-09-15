using Microsoft.EntityFrameworkCore;
using ZWarden.Application.Diagnostics;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Infrastructure.Diagnostics;

/// <summary>
/// Gathers the application database's <see cref="DatabaseProbeFacts"/> (F29) against the live
/// <see cref="ZWardenDbContext"/> — a read-only connectivity and pending-migration check. It never throws for an
/// expected database fault: a fault is reported as <see cref="DatabaseProbeFacts.CanConnect"/> = false carrying
/// the (untrusted) provider error, so one unreachable database never sinks the sweep. The pure verdict is
/// <c>DatabaseDiagnostic</c>'s.
/// </summary>
public sealed class DiagnosticsDbProbe : IDiagnosticsDbProbe
{
    private readonly ZWardenDbContext _db;

    public DiagnosticsDbProbe(ZWardenDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    /// <inheritdoc />
    public async Task<DatabaseProbeFacts> ProbeAsync(CancellationToken cancellationToken = default)
    {
        string provider = FriendlyProvider(_db.Database.ProviderName);

        try
        {
            bool canConnect = await _db.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);
            if (!canConnect)
            {
                return new DatabaseProbeFacts(CanConnect: false, PendingMigrations: 0, Provider: provider);
            }

            // Only meaningful once we can connect; it reads __EFMigrationsHistory.
            int pending = (await _db.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).Count();
            return new DatabaseProbeFacts(CanConnect: true, PendingMigrations: pending, Provider: provider);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An expected database fault (unreachable, auth, locked) is a Fail check, not a thrown sweep.
            return new DatabaseProbeFacts(CanConnect: false, PendingMigrations: 0, Provider: provider, Error: ex.Message);
        }
    }

    // ProviderName is the EF provider assembly, e.g. "Microsoft.EntityFrameworkCore.Sqlite" / "Npgsql.EntityFrameworkCore.PostgreSQL".
    private static string FriendlyProvider(string? providerName)
    {
        if (providerName is null)
        {
            return "unknown";
        }

        if (providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            return "Sqlite";
        }

        return providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ? "Postgres" : providerName;
    }
}
