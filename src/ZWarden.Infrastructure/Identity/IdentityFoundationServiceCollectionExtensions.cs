using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Infrastructure.Identity;

/// <summary>
/// Composition seam for the ASP.NET Core Identity <b>core</b> (F4): the EF-backed user/role stores, the
/// password hasher configured against OWASP guidance (ADR 0006), the NIST SP 800-63-4 password policy,
/// and the breached-password check. It has no web dependency — sign-in, cookies, and MFA challenge live
/// in the Web layer on top of this. Pair with <c>AddZWardenPersistence</c> (the <see cref="ZWardenDbContext"/>
/// the stores read) and a registered <see cref="Application.Tenancy.ITenantContext"/>.
/// </summary>
public static class IdentityFoundationServiceCollectionExtensions
{
    /// <summary>
    /// Registers Identity core over <see cref="ZWardenDbContext"/>: the user/role managers and stores,
    /// role support, the default token providers (recovery/confirmation/MFA), the explicit PBKDF2
    /// hashing parameters (<see cref="IdentityHashingParameters"/>), the NIST password policy, and the
    /// breached-password validator (default blocklist swappable via <see cref="IBreachedPasswordBlocklist"/>).
    /// </summary>
    public static IdentityBuilder AddIdentityFoundation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IBreachedPasswordBlocklist, EmbeddedBreachedPasswordBlocklist>();

        // PBKDF2-HMAC-SHA512 at the chosen (not inherited) iteration count - ADR 0006, PRD 2.1.
        services.Configure<PasswordHasherOptions>(options =>
        {
            options.CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3;
            options.IterationCount = IdentityHashingParameters.Pbkdf2IterationCount;
        });

        return services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                // NIST SP 800-63-4 (ADR 0006): a length floor, and no composition rules.
                options.Password.RequiredLength = 15;
                options.Password.RequiredUniqueChars = 1;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;

                options.User.RequireUniqueEmail = true;

                // Lockout on repeated failure; the sign-in surface (S4) enforces it.
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

                // Account recovery and MFA rely on confirmed, tokened flows (S6/S7).
                options.SignIn.RequireConfirmedAccount = true;
                options.Tokens.AuthenticatorIssuer = "ZWarden";
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<ZWardenDbContext>()
            .AddPasswordValidator<BreachedPasswordValidator>();

        // Note: the default token providers (recovery/confirmation/MFA) and SignInManager live in the
        // ASP.NET Core Identity assembly, so the Web host chains .AddDefaultTokenProviders()/.AddSignInManager()
        // onto the returned builder (S6/S7) - Infrastructure stays free of the web framework.
    }
}
