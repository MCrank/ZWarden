using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Mods;

namespace ZWarden.Infrastructure.Mods;

/// <summary>
/// Composition seam for Workshop-and-mod discovery (F21) and management (F22). Registers the in-memory inventory
/// cache, the discovery service, and the mod manager. Call it after <c>AddZWardenAuthorization</c>,
/// <c>AddZWardenOperations</c>, <c>AddZWardenServers</c>, and <c>AddZWardenConfiguration</c> — the services resolve
/// the fail-closed <c>IPermissionChecker</c>, the <c>IOperationCoordinator</c>, the tenant-scoped
/// <c>ServerRepository</c>, and (for F22) the <c>ConfigurationRevisionRepository</c> that supplies the drift baseline.
/// </summary>
public static class ModsServiceCollectionExtensions
{
    public static IServiceCollection AddZWardenMods(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IModDiscoveryService, ModDiscoveryService>();

        // The latest-inventory-per-Server cache (F21), beside the F16 metrics/health and F19 roster caches: an
        // in-process singleton that feeds the live UI. Never persisted — an inventory is display data.
        services.AddSingleton<IModInventoryCache, ModInventoryCache>();

        // F22 mutation: recomputes WorkshopItems=/Mods= from the observed inventory and enqueues an F20b
        // config-apply (or the F17 update). Reuses the config-apply enqueue + revision/drift trail.
        services.AddScoped<IServerModManager, ServerModManager>();

        return services;
    }
}
