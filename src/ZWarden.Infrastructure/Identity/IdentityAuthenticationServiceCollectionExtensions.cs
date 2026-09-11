using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ZWarden.Infrastructure.Identity;

/// <summary>
/// The authentication composition seam (F4): builds on <see cref="IdentityFoundationServiceCollectionExtensions.AddIdentityFoundation"/>
/// with the sign-in surface (SignInManager, token providers) and hardened cookie authentication, plus
/// host-header validation (ADR 0006, trust-boundaries §2). It configures services only; the HTTP
/// middleware (<c>UseAuthentication</c>/<c>UseAuthorization</c>/<c>UseHostFiltering</c>) is wired in
/// <c>Program.cs</c>.
/// </summary>
public static class IdentityAuthenticationServiceCollectionExtensions
{
    /// <summary>The fixed application session-cookie name (PRD 11 — a server-set HttpOnly cookie, never a
    /// browser-exposed bearer token).</summary>
    public const string ApplicationCookieName = "zwarden.auth";

    /// <summary>The default bounded session lifetime; sliding, so activity extends it up to this window.</summary>
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);

    /// <summary>
    /// Registers Identity's sign-in surface and hardened cookie authentication over
    /// <see cref="IdentityFoundationServiceCollectionExtensions.AddIdentityFoundation"/>, and configures
    /// host-header validation against <paramref name="allowedHosts"/> (a non-wildcard list is required —
    /// the browser boundary must not accept an arbitrary Host header).
    /// </summary>
    public static IServiceCollection AddZWardenAuthentication(
        this IServiceCollection services,
        IEnumerable<string> allowedHosts)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(allowedHosts);

        services.AddIdentityFoundation()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        // Stamp the tenant claim at sign-in from the user's own TenantId, so the session (not the
        // browser) is the source of the current tenant - read back by ClaimsPrincipalTenantContext (S5).
        services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, TenantClaimsPrincipalFactory>();

        // Account recovery (S6): the token flows need the token providers added just above; the default
        // notification sends nothing (no transport in v1.0) and is swappable behind the seam.
        services.TryAddScoped<IAccountNotification, LoggingAccountNotification>();
        services.AddScoped<AccountRecoveryService>();

        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddCookie(IdentityConstants.ApplicationScheme, ConfigureApplicationCookie)
            .AddCookie(IdentityConstants.ExternalScheme, ConfigureTransientCookie)
            .AddCookie(IdentityConstants.TwoFactorUserIdScheme, ConfigureTransientCookie)
            .AddCookie(IdentityConstants.TwoFactorRememberMeScheme, ConfigureApplicationCookie);

        services.AddAuthorization();

        string[] hosts = [.. allowedHosts];
        if (hosts.Length == 0 || hosts.Any(h => h is "*" or "" || h is null))
        {
            throw new ArgumentException(
                "Host-header validation requires at least one explicit, non-wildcard allowed host (ADR 0006).",
                nameof(allowedHosts));
        }

        services.Configure<HostFilteringOptions>(options =>
        {
            options.AllowedHosts = hosts;
            options.AllowEmptyHosts = false;
            // Do not echo the rejected host back to the client (no reflection of attacker input).
            options.IncludeFailureMessage = false;
        });

        return services;
    }

    // The persistent application session cookie: HttpOnly, always-Secure, SameSite=Lax, fixed name,
    // bounded sliding expiration (PRD 11, trust-boundaries §2).
    private static void ConfigureApplicationCookie(CookieAuthenticationOptions options)
    {
        options.Cookie.Name = ApplicationCookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = SessionLifetime;
        options.SlidingExpiration = true;
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.AccessDeniedPath = "/access-denied";
        // An unauthenticated API/interactive call gets a status code, not a redirect chain.
        options.ReturnUrlParameter = "returnUrl";
    }

    // The short-lived correlation cookies (external sign-in, two-factor user-id): HttpOnly + Secure, not
    // persisted beyond the flow.
    private static void ConfigureTransientCookie(CookieAuthenticationOptions options)
    {
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
        options.SlidingExpiration = false;
    }
}
