using Microsoft.EntityFrameworkCore;
using ZWarden.Application.Authorization;
using ZWarden.Application.Tenancy;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Infrastructure.Authorization;

/// <summary>
/// The fail-closed authorization resolver (ADR 0018). It resolves a principal's permissions by joining the
/// tenant-scoped role assignments to their roles and permission grants — <b>through the tenant-filtered
/// <see cref="Role"/> set</b>, so a dangling assignment pointing at another tenant's role resolves to
/// nothing (a cross-tenant grant can never leak). Scope semantics: a tenant-wide assignment confers the
/// role's permissions across the tenant; a server-scoped assignment confers only the role's server-scoped
/// permissions, and only on that Server; a server-scoped permission checked with no Server is denied.
/// </summary>
public sealed class PermissionChecker : IPermissionChecker
{
    private readonly ZWardenDbContext _context;
    private readonly ITenantContext _tenantContext;

    public PermissionChecker(ZWardenDbContext context, ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tenantContext);
        _context = context;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<AuthorizationDecision> EvaluateAsync(
        UserId user,
        PermissionDefinition permission,
        ServerId? server = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(permission);

        // Fail closed with no ambient tenant, before touching the store (the filter would otherwise throw).
        if (!_tenantContext.HasCurrentTenant)
        {
            return AuthorizationDecision.Deny("No authenticated tenant.");
        }

        // A server-scoped permission with no target Server never widens to "any server".
        if (permission.Scope == PermissionScope.ServerScopable && server is null)
        {
            return AuthorizationDecision.Deny($"{permission.Name} is server-scoped but no Server was supplied.");
        }

        // The scope (assignment.ServerId; null = tenant-wide) of every assignment whose role grants this
        // permission, for this user. Both DbSets carry the tenant filter, so this is tenant-scoped.
        List<ServerId?> grantingScopes = await (
            from assignment in _context.Set<RoleAssignment>()
            join role in _context.Set<Role>() on assignment.RoleId equals role.Id
            join grant in _context.Set<RolePermissionGrant>() on role.Id equals grant.RoleId
            where assignment.UserId == user && grant.PermissionName == permission.Name
            select assignment.ServerId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        bool allowed = permission.Scope == PermissionScope.TenantWide
            // A tenant-wide permission is conferred only by a tenant-wide assignment.
            ? grantingScopes.Exists(scope => scope is null)
            // A server-scoped permission: a tenant-wide grant covers every Server; otherwise the scope must match.
            : grantingScopes.Exists(scope => scope is null || scope == server);

        return allowed
            ? AuthorizationDecision.Allow(permission, permission.Scope == PermissionScope.TenantWide ? null : server)
            : AuthorizationDecision.Deny(
                server is null
                    ? $"No grant for {permission.Name} in the current tenant."
                    : $"No grant for {permission.Name} on {server} in the current tenant.");
    }
}
