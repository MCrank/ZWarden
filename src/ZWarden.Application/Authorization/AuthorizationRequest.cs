using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;

namespace ZWarden.Application.Authorization;

/// <summary>
/// The subject of an authorization decision (ADR 0018): a principal, the permission being checked, the
/// target Server (if any), and the tenant it is evaluated in. Passed to an <see cref="IAuthorizationSafetyRule"/>
/// so a safety rule can decide against the concrete request without re-deriving it.
/// </summary>
/// <param name="User">The principal being authorized.</param>
/// <param name="Permission">The permission being checked.</param>
/// <param name="Server">The target Server, or <c>null</c> for a tenant-wide check.</param>
/// <param name="Tenant">The tenant the decision is evaluated in.</param>
public sealed record AuthorizationRequest(
    UserId User,
    PermissionDefinition Permission,
    ServerId? Server,
    TenantId Tenant);
