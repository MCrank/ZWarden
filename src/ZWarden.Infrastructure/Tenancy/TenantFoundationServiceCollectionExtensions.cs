using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ZWarden.Application.Tenancy;

namespace ZWarden.Infrastructure.Tenancy;

/// <summary>
/// Composition seam for the tenant foundation (ADR 0016), mirroring <c>AddSecurityFoundation</c>. It
/// registers the ambient <see cref="ITenantContext"/>; the persistence wiring (the tenant-scoped
/// <c>ZWardenDbContext</c>) and the migrate-then-bootstrap startup path are wired by the host.
/// </summary>
public static class TenantFoundationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the self-hosted single-tenant context (<see cref="SingleTenantContext"/>): every request
    /// resolves to the default tenant. A hosted deployment replaces this with a session-derived context
    /// (F3B/F4) behind the same interface - it is registered with <see cref="TryAddScoped"/> so a
    /// prior registration wins.
    /// </summary>
    public static IServiceCollection AddTenantFoundation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<ITenantContext, SingleTenantContext>();
        return services;
    }
}
