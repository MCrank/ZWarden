using System.Runtime.InteropServices;
using ZWarden.Application.Diagnostics;
using ZWarden.Diagnostics.SupportPackage;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Web.Diagnostics;

/// <summary>
/// The Web-side <see cref="IEnvironmentFactsProvider"/> (F30): the running ZWarden build (from
/// <see cref="DiagnosticsOptions"/>, the same value the F29 base-health check reports), the configured database
/// provider (from the EF context), and the host OS and .NET runtime (from <see cref="RuntimeInformation"/>). All
/// non-secret; still sanitized/pseudonymized by the pipeline like everything else. Scoped, because it reads the
/// scoped <see cref="ZWardenDbContext"/>.
/// </summary>
public sealed class WebEnvironmentFactsProvider : IEnvironmentFactsProvider
{
    private readonly ZWardenDbContext _db;
    private readonly DiagnosticsOptions _options;

    public WebEnvironmentFactsProvider(ZWardenDbContext db, DiagnosticsOptions options)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(options);
        _db = db;
        _options = options;
    }

    /// <inheritdoc />
    public EnvironmentFacts Capture() => new(
        ZWardenVersion: _options.Version ?? "unknown",
        DatabaseProvider: FriendlyProvider(_db.Database.ProviderName),
        OperatingSystem: RuntimeInformation.OSDescription,
        RuntimeFramework: RuntimeInformation.FrameworkDescription);

    // Mirrors DiagnosticsDbProbe.FriendlyProvider: the EF provider assembly → a short label.
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
