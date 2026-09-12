using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.ControlPlane;
using ZWarden.Agent.Diagnostics;
using ZWarden.Agent.Health;
using ZWarden.Agent.Identity;
using ZWarden.Agent.Trust;

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

        // Trust (F9): the file-backed trust store and the HTTPS enrollment client, whose HttpClient targets
        // the control plane (ws/wss mapped to http/https for the one-shot enrollment POST).
        services.AddSingleton<IAgentTrustStore>(sp => new FileAgentTrustStore(
            sp.GetRequiredService<IOptions<AgentOptions>>().Value.TrustFilePath));
        services.AddSingleton<IEnrollmentClient>(sp => new HttpEnrollmentClient(
            new HttpClient { BaseAddress = ToHttpBase(sp.GetRequiredService<IOptions<AgentOptions>>().Value.ControlPlaneUri) },
            sp.GetRequiredService<ILogger<HttpEnrollmentClient>>()));

        // Control plane (F10): the outbound SignalR connection the Agent opens once enrolled.
        services.AddSingleton<IAgentControlPlaneConnection, SignalRControlPlaneConnection>();

        // Order matters: identity resolves, then enrollment runs, then the connection opens, before/while the
        // worker runs. Each hosted service's StartAsync completes before the next begins.
        services.AddHostedService<AgentIdentityInitializer>();
        services.AddHostedService<AgentEnrollmentInitializer>();
        services.AddHostedService<AgentConnectionInitializer>();
        services.AddHostedService<AgentWorker>();

        return services;
    }

    // The control-plane URI is validated as https/wss (F8); the one-shot enrollment POST is HTTP, so map the
    // WebSocket schemes to their HTTP equivalents for the client's base address.
    private static Uri ToHttpBase(string controlPlaneUri)
    {
        Uri uri = new(controlPlaneUri, UriKind.Absolute);
        string scheme = uri.Scheme switch
        {
            "wss" => Uri.UriSchemeHttps,
            "ws" => Uri.UriSchemeHttp,
            _ => uri.Scheme,
        };
        return new UriBuilder(uri) { Scheme = scheme }.Uri;
    }
}
