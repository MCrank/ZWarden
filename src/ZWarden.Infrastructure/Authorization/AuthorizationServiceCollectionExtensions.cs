using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Authorization;
using ZWarden.Application.Tenancy;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Infrastructure.Authorization;

/// <summary>
/// Composition seam for authorization (F5; ADR 0018). Registers the fail-closed decision service, the
/// role-administration service, and the ASP.NET Core enforcement surface — the dynamic policy provider and
/// the two permission handlers. The host calls this after <c>AddZWardenAuthentication</c> (the checker
/// resolves the session-derived <see cref="ITenantContext"/> and the request-scoped
/// <see cref="ZWardenDbContext"/>). Any registered <see cref="IAuthorizationSafetyRule"/> is consulted by
/// the checker; none is registered in v1.0.
/// </summary>
public static class AuthorizationServiceCollectionExtensions
{
    public static IServiceCollection AddZWardenAuthorization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAuthorization();

        // The dynamic provider turns any catalogue permission name into a policy on demand.
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

        // The decision service, explicit about its safety-rule set (empty unless a rule is registered).
        services.AddScoped<IPermissionChecker>(sp => new PermissionChecker(
            sp.GetRequiredService<ZWardenDbContext>(),
            sp.GetRequiredService<ITenantContext>(),
            sp.GetServices<IAuthorizationSafetyRule>()));

        services.AddScoped<RoleAdministrationService>();

        // Both handlers run for a PermissionRequirement; either succeeding satisfies it.
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, ServerScopedPermissionHandler>();

        return services;
    }
}
