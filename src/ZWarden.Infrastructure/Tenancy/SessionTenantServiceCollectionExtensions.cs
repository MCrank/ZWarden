using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Tenancy;

namespace ZWarden.Infrastructure.Tenancy;

/// <summary>
/// Registers the session-derived tenant context (F4). Call this <b>before</b> <c>AddTenantFoundation</c>:
/// that method's <c>TryAddScoped&lt;ITenantContext&gt;</c> then sees a registration already present and
/// leaves this one in place, so the authenticated session — not the self-hosted single-tenant default —
/// is the source of the current tenant (PRD 7A).
/// </summary>
public static class SessionTenantServiceCollectionExtensions
{
    /// <summary>Registers <see cref="ClaimsPrincipalTenantContext"/> as the scoped
    /// <see cref="ITenantContext"/>, plus the <see cref="Microsoft.AspNetCore.Http.IHttpContextAccessor"/>
    /// it reads the session principal from.</summary>
    public static IServiceCollection AddSessionTenantContext(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantContext, ClaimsPrincipalTenantContext>();
        return services;
    }
}
