using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ZWarden.Application.Operations;

namespace ZWarden.Infrastructure.Operations;

/// <summary>
/// Composition seam for the durable operations engine (F11). Registers the tenant-scoped repository, the
/// coordinator and store, the engine options, and a no-op dispatcher default. Call it after
/// <c>AddZWardenAudit</c> and the persistence registration (the services resolve the request-scoped
/// <c>ZWardenDbContext</c> and its tenant, the <c>IAuditWriter</c>, and the clock). ZWarden.Web replaces
/// <see cref="IOperationDispatcher"/> with the real SignalR dispatcher in PR-B.
/// </summary>
public static class OperationsServiceCollectionExtensions
{
    public static IServiceCollection AddZWardenOperations(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<OperationEngineOptions>();

        services.AddScoped<OperationRepository>();
        services.AddScoped<IOperationCoordinator, OperationCoordinator>();
        services.AddScoped<IOperationStore, OperationStore>();

        // The lease-expiry safety net (ADR 0022): a scoped reaper driven by a hosted timer.
        services.AddScoped<OperationReaper>();
        services.AddHostedService<OperationReaperService>();

        // No-op unless a transport is wired; Web's real dispatcher (PR-B) is registered ahead of this
        // TryAdd, so it wins.
        services.TryAddScoped<IOperationDispatcher, NullOperationDispatcher>();

        return services;
    }
}
