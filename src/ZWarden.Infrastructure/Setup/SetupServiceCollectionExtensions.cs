using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ZWarden.Application.Setup;

namespace ZWarden.Infrastructure.Setup;

/// <summary>
/// Composition seam for first-run setup (F33). Registers the process-wide completion memo and the scoped
/// <see cref="ISetupState"/> over the <see cref="ZWarden.Domain.Setup.InstallState"/> singleton. The host
/// calls this once alongside the other feature registrations.
/// </summary>
public static class SetupServiceCollectionExtensions
{
    public static IServiceCollection AddZWardenSetup(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<SetupCompletionSignal>();
        services.AddScoped<ISetupState, SetupState>();
        return services;
    }
}
