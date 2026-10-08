using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Settings;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Settings;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Identity;

/// <summary>
/// Applies the Owner's session idle timeout (#346, ADR 0048) to the application session cookie without a restart. The
/// cookie handler reads <c>ExpireTimeSpan</c> only at sign-in, and a sliding renewal keeps the ticket's own span, so
/// the timeout is applied here: a sign-in gets <c>ExpiresUtc = IssuedUtc + timeout</c>, and on every request a ticket
/// whose span differs from the current timeout is either ended (idle longer than the new timeout since its last
/// renewal) or re-spanned and renewed. The timeout comes from the settings cache (no query per request) for the
/// principal's tenant; with no cache, tenant or override it is <see cref="ControlPlaneSettings.DefaultSessionIdleTimeout"/>.
/// </summary>
public static class SessionTimeoutCookieEvents
{
    /// <summary>Sets a new session's expiry from the tenant's idle timeout.</summary>
    public static async Task SigningInAsync(CookieSigningInContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        TimeSpan timeout = await TimeoutForAsync(context.HttpContext, context.Principal).ConfigureAwait(false);
        DateTimeOffset issued = context.Properties.IssuedUtc ?? Now(context.Options);
        context.Properties.IssuedUtc = issued;
        context.Properties.ExpiresUtc = issued + timeout;
    }

    /// <summary>The application cookie's principal check: the security stamp first (#297), then the idle timeout.</summary>
    public static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        await SecurityStampValidator.ValidatePrincipalAsync(context).ConfigureAwait(false);
        if (context.Principal is not null)
        {
            await ApplyTimeoutAsync(context).ConfigureAwait(false);
        }
    }

    /// <summary>Brings a live ticket in line with the tenant's current idle timeout.</summary>
    public static async Task ApplyTimeoutAsync(CookieValidatePrincipalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Properties.IssuedUtc is not { } issued || context.Properties.ExpiresUtc is not { } expires)
        {
            return;
        }

        TimeSpan timeout = await TimeoutForAsync(context.HttpContext, context.Principal).ConfigureAwait(false);
        if (expires - issued == timeout)
        {
            return;
        }

        if (Now(context.Options) - issued >= timeout)
        {
            // Idle for longer than the new timeout: end the session as if it had expired.
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(context.Scheme.Name).ConfigureAwait(false);
            return;
        }

        // A sliding renewal keeps the ticket's span, so set the new one and renew from now.
        context.Properties.ExpiresUtc = issued + timeout;
        context.ShouldRenew = true;
    }

    private static DateTimeOffset Now(CookieAuthenticationOptions options) =>
        (options.TimeProvider ?? TimeProvider.System).GetUtcNow();

    private static async Task<TimeSpan> TimeoutForAsync(HttpContext http, ClaimsPrincipal? principal)
    {
        IControlPlaneSettingsCache? cache = http.RequestServices.GetService<IControlPlaneSettingsCache>();
        string? tenantClaim = principal?.FindFirst(ClaimsPrincipalTenantContext.TenantClaimType)?.Value;
        if (cache is null || tenantClaim is null || !TenantId.TryParse(tenantClaim, out TenantId tenant))
        {
            return ControlPlaneSettings.DefaultSessionIdleTimeout;
        }

        ControlPlaneSettingsSnapshot settings = await cache.GetAsync(tenant, http.RequestAborted).ConfigureAwait(false);
        return settings.SessionIdleTimeout ?? ControlPlaneSettings.DefaultSessionIdleTimeout;
    }
}
