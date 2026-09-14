using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Mods;

namespace ZWarden.Infrastructure.Mods;

/// <summary>
/// Composition seam for Workshop-and-mod discovery (F21). Registers the in-memory inventory cache and the
/// operator-facing discovery service. Call it after <c>AddZWardenAuthorization</c>, <c>AddZWardenOperations</c>,
/// and <c>AddZWardenServers</c> — the service resolves the fail-closed <c>IPermissionChecker</c>, the
/// <c>IOperationCoordinator</c>, and the tenant-scoped <c>ServerRepository</c>.
/// </summary>
public static class ModsServiceCollectionExtensions
{
    public static IServiceCollection AddZWardenMods(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The latest-inventory-per-Server cache (F21), beside the F16 metrics/health and F19 roster caches: an
        // in-process singleton that feeds the live UI. Never persisted — an inventory is display data.
        services.AddSingleton<IModInventoryCache, ModInventoryCache>();

        return services;
    }
}
