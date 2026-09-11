using Microsoft.EntityFrameworkCore;
using ZWarden.Domain.Tenancy;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Infrastructure.Tenancy;

/// <summary>
/// Seeds the single-tenant self-hosted default (ADR 0016): idempotently inserts the <see cref="Tenant"/>
/// row under the fixed <see cref="Tenant.DefaultId"/> so F9 (Agent enrollment, which binds to a tenant)
/// has a tenant to bind to without waiting on v1.1 administration. Runs at startup after migration, and
/// is the sanctioned unscoped read of the <c>Tenants</c> table (which is not tenant-owned). Assumes the
/// single-writer startup path (ADR 0005); a concurrent insert would surface as a primary-key conflict.
/// </summary>
public static class TenantBootstrapper
{
    /// <summary>Inserts the default tenant if it is absent; a no-op if it already exists.</summary>
    public static async Task EnsureDefaultTenantAsync(
        ZWardenDbContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        bool exists = await context.Set<Tenant>()
            .AnyAsync(t => t.Id == Tenant.DefaultId, cancellationToken)
            .ConfigureAwait(false);
        if (exists)
        {
            return;
        }

        context.Add(Tenant.CreateDefault());
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
