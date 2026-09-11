using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ZWarden.Domain;

namespace ZWarden.Infrastructure.Persistence;

/// <summary>
/// Stamps a fresh <see cref="IVersioned.Version"/> on every inserted or modified entity (ADR 0005:
/// assign-on-write). EF uses the entity's ORIGINAL version in the UPDATE's WHERE and this new value
/// in its SET, so a write against a stale row matches nothing and raises
/// <see cref="DbUpdateConcurrencyException"/> - identically on SQLite and PostgreSQL.
/// </summary>
public sealed class VersionStampingInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries<IVersioned>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.Version = Guid.NewGuid();
            }
        }
    }
}
