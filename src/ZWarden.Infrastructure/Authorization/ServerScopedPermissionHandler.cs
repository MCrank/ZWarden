using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using ZWarden.Application.Authorization;
using ZWarden.Domain.Ids;

namespace ZWarden.Infrastructure.Authorization;

/// <summary>
/// Satisfies a <see cref="PermissionRequirement"/> against a specific Server (ADR 0018): when authorization
/// is evaluated with an <see cref="IServerScoped"/> resource, this handler asks the fail-closed
/// <see cref="IPermissionChecker"/> whether the authenticated user holds the permission <b>on that
/// Server</b>. A grant scoped to another Server, or no grant at all, leaves the requirement unmet.
/// Runs alongside <see cref="PermissionAuthorizationHandler"/>: either succeeding satisfies the requirement,
/// so a tenant-wide grant (which the other handler recognizes) still authorizes a server-scoped action.
/// </summary>
public sealed class ServerScopedPermissionHandler
    : AuthorizationHandler<PermissionRequirement, IServerScoped>
{
    private readonly IPermissionChecker _checker;
    private readonly string _userIdClaimType;

    public ServerScopedPermissionHandler(IPermissionChecker checker, IOptions<IdentityOptions> identityOptions)
    {
        ArgumentNullException.ThrowIfNull(checker);
        ArgumentNullException.ThrowIfNull(identityOptions);
        _checker = checker;
        _userIdClaimType = identityOptions.Value.ClaimsIdentity.UserIdClaimType;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement,
        IServerScoped resource)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(resource);

        if (!context.User.TryGetUserId(_userIdClaimType, out UserId user))
        {
            return;
        }

        AuthorizationDecision decision =
            await _checker.EvaluateAsync(user, requirement.Permission, resource.ServerId).ConfigureAwait(false);
        if (decision.IsAllowed)
        {
            context.Succeed(requirement);
        }
    }
}
