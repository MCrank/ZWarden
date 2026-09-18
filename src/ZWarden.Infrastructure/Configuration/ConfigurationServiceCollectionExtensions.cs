using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Configuration;

namespace ZWarden.Infrastructure.Configuration;

/// <summary>
/// Composition seam for configuration revisions (F20b). Registers the tenant-scoped
/// <see cref="ConfigurationRevisionRepository"/> (its DI was deferred from PR 2 to its first consumer) and the
/// completion-time <see cref="IConfigurationRevisionRecorder"/>. Call it after <c>AddZWardenServers</c> — the
/// recorder resolves the tenant-scoped <c>ServerRepository</c>, the request-scoped <c>ZWardenDbContext</c>, and
/// the clock.
/// </summary>
public static class ConfigurationServiceCollectionExtensions
{
    public static IServiceCollection AddZWardenConfiguration(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ConfigurationRevisionRepository>();
        services.AddScoped<IConfigurationRevisionRecorder, ConfigurationRevisionRecorder>();
        services.AddScoped<IServerConfigurationEditor, ServerConfigurationEditor>();
        services.AddScoped<IServerConfigurationHistory, ConfigurationHistoryService>();
        // Live Configuration View reader (F20c, ADR 0041). It depends on IServerConfigReadChannel, which
        // ZWarden.Web supplies (the tier that owns the Agent hub connection).
        services.AddScoped<IServerConfigurationReader, ServerConfigurationReader>();

        return services;
    }
}
