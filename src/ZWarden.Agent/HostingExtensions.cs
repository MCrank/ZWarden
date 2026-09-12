using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.Diagnostics;
using ZWarden.Agent.Health;
using ZWarden.Agent.Identity;

namespace ZWarden.Agent;

/// <summary>
/// Composition of the Agent runtime skeleton (F8): configuration, self-identity, health state,
/// diagnostics and the worker. Kept in one place so the wiring is auditable and the startup order
/// (identity resolved before the worker runs) is explicit.
/// </summary>
public static class HostingExtensions
{
    /// <summary>Registers the Agent runtime services and hosted lifecycle.</summary>
    public static IServiceCollection AddAgentRuntime(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptionsWithValidateOnStart<AgentOptions>()
            .Bind(configuration.GetSection(AgentOptions.SectionName))
            .ValidateDataAnnotations();
        services.AddSingleton<IValidateOptions<AgentOptions>, AgentOptionsValidator>();

        services.AddSingleton(TimeProvider.System);

        services.AddSingleton<IAgentIdentityStore>(sp => new FileAgentIdentityStore(
            sp.GetRequiredService<IOptions<AgentOptions>>().Value.IdentityFilePath,
            sp.GetRequiredService<ILogger<FileAgentIdentityStore>>()));
        services.AddSingleton<AgentIdentityHolder>();
        services.AddSingleton<IAgentIdentity>(sp => sp.GetRequiredService<AgentIdentityHolder>());

        services.AddSingleton<IAgentHealthState, AgentHealthState>();
        services.AddSingleton<IAgentDiagnostics, AgentDiagnostics>();

        // Order matters: the initializer resolves the identity before the worker's banner reads it.
        services.AddHostedService<AgentIdentityInitializer>();
        services.AddHostedService<AgentWorker>();

        return services;
    }
}
