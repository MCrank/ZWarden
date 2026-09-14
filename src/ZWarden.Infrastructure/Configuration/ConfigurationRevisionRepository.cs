using Microsoft.EntityFrameworkCore;
using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Configuration;

/// <summary>
/// A tenant-scoped repository over <see cref="ConfigurationRevision"/> (ADR 0016, F20b). Every read builds on
/// the filtered query root, so no path returns another tenant's revision history.
/// </summary>
public sealed class ConfigurationRevisionRepository : TenantScopedRepository<ConfigurationRevision>
{
    public ConfigurationRevisionRepository(ZWardenDbContext context)
        : base(context)
    {
    }

    /// <summary>The ambient tenant's revision with the given id, or <c>null</c>.</summary>
    public async Task<ConfigurationRevision?> FindByIdAsync(
        ConfigurationRevisionId id, CancellationToken cancellationToken = default)
        => await Entities.FirstOrDefaultAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false);

    /// <summary>The most recent revision recorded for a Server's <paramref name="file"/>, or <c>null</c> when
    /// none has been recorded yet — the drift baseline the next write re-checks (ADR 0011).</summary>
    public async Task<ConfigurationRevision?> FindLatestAsync(
        ServerId server, PzConfigFile file, CancellationToken cancellationToken = default)
        => await Entities
            .Where(r => r.ServerId == server && r.File == file)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>The ambient tenant's revision history for a Server's <paramref name="file"/>, newest first —
    /// the revision-history read (PR 4).</summary>
    public async Task<IReadOnlyList<ConfigurationRevision>> ListForFileAsync(
        ServerId server, PzConfigFile file, CancellationToken cancellationToken = default)
        => await Entities
            .Where(r => r.ServerId == server && r.File == file)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}
