using Microsoft.Extensions.DependencyInjection;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;

namespace ZWarden.Infrastructure.Tenancy;

/// <summary>
/// The only place in <c>src</c> that opens a DI scope (#297, ADR 0046 Q6; arch-tested). Every scope it opens
/// carries an explicit tenant, so code running outside a request (startup, background work, a child scope of a
/// circuit) never relies on a fallback.
/// </summary>
public static class TenantScopes
{
    /// <summary>Opens a scope whose tenant is <paramref name="tenant"/>, e.g. a child scope that must keep its
    /// caller's tenant (<see cref="Authorization.ScopedPermissionChecker"/>).</summary>
    public static AsyncServiceScope CreateTenantScope(this IServiceScopeFactory scopeFactory, TenantId tenant)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantAssignment>().Assign(tenant);
        return scope;
    }

    /// <inheritdoc cref="CreateTenantScope(IServiceScopeFactory, TenantId)"/>
    public static AsyncServiceScope CreateTenantScope(this IServiceProvider services, TenantId tenant)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.GetRequiredService<IServiceScopeFactory>().CreateTenantScope(tenant);
    }

    /// <summary>
    /// Opens a <b>system scope</b> for the install's default tenant: the named single-tenant path for startup
    /// bootstrappers and background services. A hosted deployment (F3B) iterates tenants instead.
    /// </summary>
    public static AsyncServiceScope CreateSystemScope(this IServiceScopeFactory scopeFactory)
        => scopeFactory.CreateTenantScope(Tenant.DefaultId);

    /// <inheritdoc cref="CreateSystemScope(IServiceScopeFactory)"/>
    public static AsyncServiceScope CreateSystemScope(this IServiceProvider services)
        => services.CreateTenantScope(Tenant.DefaultId);
}
