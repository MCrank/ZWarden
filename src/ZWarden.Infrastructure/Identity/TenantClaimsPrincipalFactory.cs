using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Identity;

/// <summary>
/// Stamps the tenant claim onto the session principal at sign-in, from the user's own
/// <see cref="ApplicationUser.TenantId"/> — so the current tenant is derived from who signed in, never
/// from anything the browser sends (PRD 7A). <see cref="ClaimsPrincipalTenantContext"/> reads it back.
/// </summary>
public sealed class TenantClaimsPrincipalFactory
    : UserClaimsPrincipalFactory<ApplicationUser, ApplicationRole>
{
    public TenantClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IOptions<IdentityOptions> optionsAccessor)
        : base(userManager, roleManager, optionsAccessor)
    {
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        ClaimsIdentity identity = await base.GenerateClaimsAsync(user).ConfigureAwait(false);
        identity.AddClaim(new Claim(ClaimsPrincipalTenantContext.TenantClaimType, user.TenantId.ToString()));
        return identity;
    }
}
