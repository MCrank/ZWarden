using Microsoft.EntityFrameworkCore;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Infrastructure.Authorization;

/// <summary>
/// Seeds a tenant's built-in roles (PRD 12A; ADR 0018), idempotently, and optionally grants a bootstrap
/// user the <b>Tenant Owner</b> role so a fresh self-hosted install has a fully-capable administrator from
/// first boot. Seeds for the context's current tenant, so it is correct for the self-hosted default tenant
/// and for per-tenant seeding in a hosted deployment. Safe to run on every boot: missing built-in roles
/// are added, the owner assignment is created only if absent.
/// </summary>
public static class BuiltInRoleSeeder
{
    public static async Task EnsureSeededAsync(
        ZWardenDbContext context,
        UserId? tenantOwnerUser = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        TenantId tenant = context.CurrentTenantId;

        List<BuiltInRoleKind?> existing = await context.Set<Role>()
            .Where(r => r.BuiltIn != null)
            .Select(r => r.BuiltIn)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        bool added = false;
        foreach (BuiltInRoleDefinition definition in BuiltInRoles.All)
        {
            if (!existing.Contains(definition.Kind))
            {
                context.Add(Role.FromBuiltIn(tenant, definition));
                added = true;
            }
        }

        if (added)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        if (tenantOwnerUser is not UserId owner)
        {
            return;
        }

        Role ownerRole = await context.Set<Role>()
            .FirstAsync(r => r.BuiltIn == BuiltInRoleKind.TenantOwner, cancellationToken)
            .ConfigureAwait(false);

        bool alreadyAssigned = await context.Set<RoleAssignment>()
            .AnyAsync(a => a.UserId == owner && a.RoleId == ownerRole.Id, cancellationToken)
            .ConfigureAwait(false);
        if (!alreadyAssigned)
        {
            context.Add(RoleAssignment.TenantWide(tenant, owner, ownerRole.Id));
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
