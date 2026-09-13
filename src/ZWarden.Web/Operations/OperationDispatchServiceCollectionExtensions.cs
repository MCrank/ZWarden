using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Operations;

namespace ZWarden.Web.Operations;

/// <summary>
/// Wires the real Web <see cref="IOperationDispatcher"/> (F11 PR-B) over the F10 SignalR connection. Call it
/// <b>after</b> <c>AddZWardenOperations</c> (which registers the no-op default via <c>TryAdd</c>) and
/// <c>AddAgentControlPlane</c> (which registers the hub, its <c>IHubContext</c> and the connection registry),
/// so this scoped registration wins and its dependencies resolve.
/// </summary>
public static class OperationDispatchServiceCollectionExtensions
{
    public static IServiceCollection AddOperationDispatch(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IOperationDispatcher, OperationDispatcher>();
        return services;
    }
}
