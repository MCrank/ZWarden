using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ZWarden.Application.Servers;
using ZWarden.Infrastructure.Agents;

namespace ZWarden.Infrastructure.Servers;

/// <summary>
/// Composition seam for Server registration and inventory (F14). Registers the tenant-scoped
/// <see cref="ServerRepository"/>, the operator-facing <see cref="IServerInventory"/>, the snapshot
/// <see cref="IServerStateReconciler"/>, and the process-local <see cref="IServerDiscoveryCache"/> (a
/// singleton, like the F10 connection registry). Call it after <c>AddZWardenAuthorization</c> and
/// <c>AddZWardenAudit</c> (the inventory resolves the fail-closed <c>IPermissionChecker</c>, the
/// <c>IAuditWriter</c>, the request-scoped <c>ZWardenDbContext</c> and its tenant, and the clock).
/// </summary>
public static class ServersServiceCollectionExtensions
{
    public static IServiceCollection AddZWardenServers(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<ServerRepository>();
        // Also needed by the inventory to confirm an Agent exists; enrollment registers it too — TryAdd keeps
        // this seam self-contained without double-registering.
        services.TryAddScoped<AgentRepository>();

        services.AddScoped<IServerInventory, ServerInventory>();
        services.AddScoped<IServerLifecycle, ServerLifecycle>();
        services.AddScoped<IServerDiagnostics, ServerDiagnostics>();
        services.AddScoped<IServerStateReconciler, ServerStateReconciler>();

        // The discovery cache holds the last snapshot per Agent in-process; a singleton, like the connection
        // registry it sits beside.
        services.AddSingleton<IServerDiscoveryCache, ServerDiscoveryCache>();

        // The latest-sample-per-Server metrics cache (F16): transient, in-process, a singleton beside the
        // discovery cache. Never persisted — metrics are display data, not durable state.
        services.AddSingleton<IServerMetricsCache, ServerMetricsCache>();

        // The latest-health-per-Server cache (F16), beside the metrics cache: lets an interactive circuit show
        // live health without a tenant-scoped read. Transient; the durable value is Server.LastHealth.
        services.AddSingleton<IServerHealthCache, ServerHealthCache>();

        return services;
    }
}
