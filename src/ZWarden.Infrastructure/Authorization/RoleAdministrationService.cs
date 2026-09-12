using Microsoft.EntityFrameworkCore;
using ZWarden.Application.Authorization;
using ZWarden.Application.Tenancy;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Infrastructure.Authorization;

/// <summary>
/// Manages a tenant's roles (PRD 12A; ADR 0018). Every operation requires the actor to hold
/// <c>Role.Manage</c> (tenant-wide), and enforces <b>no self-escalation</b>: an actor may only add a
/// permission to a role that the actor itself holds tenant-wide — so a role cannot be used to bootstrap a
/// capability its author lacks. Built-in roles may have their bundle adjusted (PRD 12A) but not deleted
/// (they would re-seed). Roles created here are tenant-owned (stamped by the ownership interceptor).
/// </summary>
public sealed class RoleAdministrationService
{
    private readonly ZWardenDbContext _context;
    private readonly IPermissionChecker _checker;
    private readonly ITenantContext _tenantContext;

    public RoleAdministrationService(
        ZWardenDbContext context,
        IPermissionChecker checker,
        ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(checker);
        ArgumentNullException.ThrowIfNull(tenantContext);
        _context = context;
        _checker = checker;
        _tenantContext = tenantContext;
    }

    /// <summary>Creates a tenant-owned custom role with the given permission bundle.</summary>
    public async Task<Role> CreateRoleAsync(
        UserId actor,
        string name,
        IReadOnlyCollection<PermissionDefinition> permissions,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(permissions);

        IReadOnlySet<string> held = await RequireRoleManageAsync(actor, cancellationToken).ConfigureAwait(false);
        RequireNoEscalation(held, permissions);

        Role role = Role.CreateCustom(_tenantContext.CurrentTenantId, name);
        foreach (PermissionDefinition permission in permissions)
        {
            role.Grant(permission);
        }

        _context.Add(role);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return role;
    }

    /// <summary>Replaces a role's permission bundle with the requested set. Removals are unrestricted;
    /// only newly-added permissions are subject to the no-escalation rule.</summary>
    public async Task AdjustRoleAsync(
        UserId actor,
        RoleId roleId,
        IReadOnlyCollection<PermissionDefinition> permissions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        IReadOnlySet<string> held = await RequireRoleManageAsync(actor, cancellationToken).ConfigureAwait(false);

        Role role = await _context.Set<Role>()
            .Include(r => r.Permissions)
            .FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new AuthorizationDeniedException("Role not found in the current tenant.");

        HashSet<string> currentNames = role.Permissions.Select(g => g.PermissionName).ToHashSet(StringComparer.Ordinal);
        HashSet<string> requestedNames = permissions.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        List<PermissionDefinition> added = permissions.Where(p => !currentNames.Contains(p.Name)).ToList();
        RequireNoEscalation(held, added);

        foreach (PermissionDefinition permission in added)
        {
            role.Grant(permission);
        }

        foreach (string removed in currentNames.Where(n => !requestedNames.Contains(n)))
        {
            if (Permissions.TryGet(removed, out PermissionDefinition definition))
            {
                role.Revoke(definition);
            }
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes a custom role and its assignments. Built-in roles cannot be deleted.</summary>
    public async Task DeleteRoleAsync(UserId actor, RoleId roleId, CancellationToken cancellationToken = default)
    {
        await RequireRoleManageAsync(actor, cancellationToken).ConfigureAwait(false);

        Role role = await _context.Set<Role>()
            .FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new AuthorizationDeniedException("Role not found in the current tenant.");

        if (role.IsBuiltIn)
        {
            throw new AuthorizationDeniedException("Built-in roles cannot be deleted.");
        }

        // No relational FK: remove the role's assignments explicitly (tenant-filtered).
        List<RoleAssignment> assignments = await _context.Set<RoleAssignment>()
            .Where(a => a.RoleId == roleId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        _context.RemoveRange(assignments);
        _context.Remove(role);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlySet<string>> RequireRoleManageAsync(UserId actor, CancellationToken cancellationToken)
    {
        IReadOnlySet<string> held = await _checker.GetTenantWidePermissionsAsync(actor, cancellationToken).ConfigureAwait(false);
        if (!held.Contains(Permissions.RoleManage.Name))
        {
            throw new AuthorizationDeniedException($"{Permissions.RoleManage.Name} is required to manage roles.");
        }

        return held;
    }

    private static void RequireNoEscalation(IReadOnlySet<string> held, IEnumerable<PermissionDefinition> added)
    {
        foreach (PermissionDefinition permission in added)
        {
            if (!held.Contains(permission.Name))
            {
                throw new AuthorizationDeniedException(
                    $"Cannot grant {permission.Name}: the actor does not hold it tenant-wide.");
            }
        }
    }
}
