using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Console;

namespace ZWarden.Infrastructure.Console;

/// <summary>
/// Composition seam for the remote administrative console (F28). Registers the tenant-scoped
/// <see cref="IConsoleCommandService"/> and the in-process <see cref="IConsoleOutputCache"/>. Call it after
/// <c>AddZWardenAuthorization</c>, <c>AddZWardenAudit</c>, <c>AddZWardenOperations</c>, and <c>AddZWardenServers</c>
/// — the service resolves the fail-closed <c>IPermissionChecker</c>, the <c>IAuditWriter</c>, the
/// <c>IOperationCoordinator</c>, and the tenant-scoped <c>ServerRepository</c>.
/// </summary>
public static class ConsoleServiceCollectionExtensions
{
    public static IServiceCollection AddZWardenConsole(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IConsoleCommandService, ConsoleCommandService>();

        // The latest-outputs-per-Server cache (F28), beside the F19 roster cache: an in-process singleton that
        // feeds the live console pane. Never persisted — output is display data, and the audit trail is the
        // durable history (ADR 0032).
        services.AddSingleton<IConsoleOutputCache, ConsoleOutputCache>();

        return services;
    }
}
