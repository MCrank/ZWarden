using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;

namespace ZWarden.Infrastructure.Persistence;

/// <summary>
/// Enforces the ownership rule on every save (ADR 0016), beside the <see cref="VersionStampingInterceptor"/>.
/// On an inserted <see cref="ITenantOwned"/> entity it stamps the ambient tenant when unset and rejects a
/// foreign tenant when set; on a modified one it rejects any change to the tenant scope (immutability) and
/// any write to a row not owned by the ambient tenant. It reads the ambient tenant from the executing
/// <see cref="ZWardenDbContext"/>, so it fails closed exactly where the context does.
/// </summary>
public sealed class TenantScopeInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Enforce(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Enforce(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Enforce(DbContext? context)
    {
        if (context is not ZWardenDbContext zw)
        {
            return;
        }

        foreach (var entry in zw.ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            var property = entry.Property(e => e.TenantId);

            if (entry.State == EntityState.Added)
            {
                TenantId assigned = property.CurrentValue;
                if (assigned.IsEmpty)
                {
                    property.CurrentValue = zw.CurrentTenantId; // stamp the ambient tenant
                }
                else if (assigned != zw.CurrentTenantId)
                {
                    throw new TenantScopeViolationException(TenantScopeViolation.ForeignTenantInsert);
                }
            }
            else
            {
                if (property.OriginalValue != property.CurrentValue)
                {
                    throw new TenantScopeViolationException(TenantScopeViolation.ScopeMutation);
                }

                if (property.CurrentValue != zw.CurrentTenantId)
                {
                    throw new TenantScopeViolationException(TenantScopeViolation.ForeignTenantWrite);
                }
            }
        }
    }
}
