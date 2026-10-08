using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Settings;

namespace ZWarden.Infrastructure.Settings;

/// <summary>Registers the operator-edited control-plane settings store (#345, ADR 0048).</summary>
public static class ControlPlaneSettingsServiceCollectionExtensions
{
    public static IServiceCollection AddZWardenControlPlaneSettings(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<ControlPlaneSettingsRepository>();
        services.AddScoped<IControlPlaneSettingsService, ControlPlaneSettingsService>();
        services.AddSingleton<IControlPlaneSettingsCache, ControlPlaneSettingsCache>();
        return services;
    }
}
