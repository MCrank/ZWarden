using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using ZWarden.Application.Authorization;
using ZWarden.Domain.Ids;

namespace ZWarden.Infrastructure.Authorization;

/// <summary>
/// Satisfies a <see cref="PermissionRequirement"/> for a <b>tenant-wide</b> check (no resource) by asking
/// the fail-closed <see cref="IPermissionChecker"/> whether the authenticated user holds the permission in
/// the current tenant (ADR 0018). The tenant is ambient (derived from the session, F4); this handler
/// supplies no Server, so a server-scopable permission checked without a resource fails closed here — the
/// server-scoped resource handler is what authorizes those. Never succeeding simply leaves the requirement
/// unmet (deny); it never blocks another handler that legitimately succeeds.
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IPermissionChecker _checker;
    private readonly string _userIdClaimType;

    public PermissionAuthorizationHandler(IPermissionChecker checker, IOptions<IdentityOptions> identityOptions)
    {
        ArgumentNullException.ThrowIfNull(checker);
        ArgumentNullException.ThrowIfNull(identityOptions);
        _checker = checker;
        _userIdClaimType = identityOptions.Value.ClaimsIdentity.UserIdClaimType;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (!context.User.TryGetUserId(_userIdClaimType, out UserId user))
        {
            return;
        }

        AuthorizationDecision decision =
            await _checker.EvaluateAsync(user, requirement.Permission).ConfigureAwait(false);
        if (decision.IsAllowed)
        {
            context.Succeed(requirement);
        }
    }
}
