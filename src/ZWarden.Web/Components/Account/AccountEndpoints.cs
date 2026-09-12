using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ZWarden.Infrastructure.Identity;

namespace ZWarden.Web.Components.Account;

/// <summary>
/// Minimal-API endpoints behind the Account pages that must act on the raw HTTP request rather than a
/// component (F4 UI, issue #63). Sign-out clears the auth cookie, which — like sign-in — can only happen
/// on a real response, not an interactive circuit.
/// </summary>
public static class AccountEndpoints
{
    /// <summary>Maps the Account endpoints (currently the sign-out POST) under <c>/</c>.</summary>
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // POST /logout: binding [FromForm] opts the endpoint into antiforgery validation, so the
        // sign-out form must post the antiforgery token (CSRF-protected sign-out).
        endpoints.MapPost("/logout", async (
            SignInManager<ApplicationUser> signInManager,
            [FromForm] string? returnUrl) =>
        {
            await signInManager.SignOutAsync().ConfigureAwait(false);
            return TypedResults.LocalRedirect($"~/{IdentityRedirectManager.ToLocalOrHome(returnUrl).TrimStart('/')}");
        });

        return endpoints;
    }
}
