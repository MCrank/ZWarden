using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Identity;

/// <summary>
/// Decides whether a signed-in principal is still a live session (#297, ADR 0046 Q3): the user still exists in
/// the principal's own tenant, the principal's security stamp still matches, and the user still holds at least
/// one role assignment. An open interactive page re-asks every <see cref="Interval"/>; the session cookie checks
/// the stamp on the same interval. Fails closed: a principal with no tenant or user claim is not valid.
/// </summary>
public sealed class SessionRevalidator
{
    /// <summary>How often an open circuit and the session cookie re-check the session (maintainer decision D4).</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<IdentityOptions> _identityOptions;

    public SessionRevalidator(IServiceScopeFactory scopeFactory, IOptions<IdentityOptions> identityOptions)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(identityOptions);
        _scopeFactory = scopeFactory;
        _identityOptions = identityOptions;
    }

    /// <summary>Whether <paramref name="principal"/> is still a valid session.</summary>
    public async Task<bool> IsStillValidAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        string? tenantClaim = principal.FindFirst(ClaimsPrincipalTenantContext.TenantClaimType)?.Value;
        if (tenantClaim is null || !TenantId.TryParse(tenantClaim, out TenantId tenant))
        {
            return false;
        }

        // A fresh scope in the principal's own tenant: the caller is a long-lived circuit, and its scope's
        // context must not be shared with this check.
        await using AsyncServiceScope scope = _scopeFactory.CreateTenantScope(tenant);
        UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        ApplicationUser? user = await users.GetUserAsync(principal).ConfigureAwait(false);
        if (user is null)
        {
            return false;
        }

        if (users.SupportsUserSecurityStamp)
        {
            string? presented = principal.FindFirst(_identityOptions.Value.ClaimsIdentity.SecurityStampClaimType)?.Value;
            string current = await users.GetSecurityStampAsync(user).ConfigureAwait(false);
            if (!string.Equals(presented, current, StringComparison.Ordinal))
            {
                return false;
            }
        }

        UserId userId = user.UserId;
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        return await db.Set<RoleAssignment>().AnyAsync(a => a.UserId == userId, cancellationToken).ConfigureAwait(false);
    }
}
