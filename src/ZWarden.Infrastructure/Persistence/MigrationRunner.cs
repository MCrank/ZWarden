using Microsoft.EntityFrameworkCore;

namespace ZWarden.Infrastructure.Persistence;

/// <summary>
/// Applies pending EF Core migrations on startup, idempotently (ADR 0005). Opt-out via
/// <paramref name="enabled"/> so a deployment can migrate out-of-band. Per-provider migration
/// assemblies are selected by the provider configuration; the first production migration - and thus
/// the two-assembly per-provider set - is Feature 3A's (mini-plan Q4).
/// </summary>
public static class MigrationRunner
{
    public static async Task EnsureMigratedAsync(
        DbContext context,
        bool enabled = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!enabled)
        {
            return;
        }

        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }
}
