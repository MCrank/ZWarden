using Microsoft.EntityFrameworkCore;
using ZWarden.Domain.Settings;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Settings;

/// <summary>
/// A tenant-scoped repository over <see cref="ControlPlaneSettings"/> (ADR 0016, ADR 0048). Every read builds on the
/// filtered query root, so it only ever sees the ambient tenant's single row; the unique index on <c>TenantId</c>
/// guarantees there is at most one.
/// </summary>
public sealed class ControlPlaneSettingsRepository : TenantScopedRepository<ControlPlaneSettings>
{
    public ControlPlaneSettingsRepository(ZWardenDbContext context)
        : base(context)
    {
    }

    /// <summary>The ambient tenant's settings row, or <c>null</c> when nothing has ever been set.</summary>
    public async Task<ControlPlaneSettings?> GetAsync(CancellationToken cancellationToken = default)
        => await Entities.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
}
