using System.Security.Claims;
using ZWarden.Domain.Ids;

namespace ZWarden.Infrastructure.Authorization;

/// <summary>Reads the authenticated <see cref="UserId"/> off a principal, from the Identity user-id claim
/// (F4 stamps it; the claim type is <c>IdentityOptions.ClaimsIdentity.UserIdClaimType</c>). Fails closed:
/// a missing or malformed claim yields no user, so the authorization handlers do not succeed.</summary>
internal static class PrincipalExtensions
{
    public static bool TryGetUserId(this ClaimsPrincipal principal, string claimType, out UserId userId)
    {
        userId = default;
        string? value = principal?.FindFirst(claimType)?.Value;
        if (Guid.TryParse(value, out Guid guid) && guid != Guid.Empty)
        {
            userId = UserId.FromGuid(guid);
            return true;
        }

        return false;
    }
}
