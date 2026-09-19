using Microsoft.EntityFrameworkCore;
using ZWarden.Domain.Workshop;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Workshop;

/// <summary>
/// A tenant-scoped repository over <see cref="WorkshopIntegrationSettings"/> (ADR 0016). Every read builds on
/// the filtered query root, so it can only ever see the ambient tenant's single row — the unique index on
/// <c>TenantId</c> guarantees there is at most one.
/// </summary>
public sealed class WorkshopIntegrationSettingsRepository : TenantScopedRepository<WorkshopIntegrationSettings>
{
    public WorkshopIntegrationSettingsRepository(ZWardenDbContext context)
        : base(context)
    {
    }

    /// <summary>The ambient tenant's settings row, or <c>null</c> when the tenant has never configured a key.</summary>
    public async Task<WorkshopIntegrationSettings?> GetAsync(CancellationToken cancellationToken = default)
        => await Entities.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
}
