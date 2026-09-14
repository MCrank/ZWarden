using Docker.DotNet;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.ControlPlane;
using ZWarden.Agent.Diagnostics;
using ZWarden.Agent.Docker;
using ZWarden.Agent.Health;
using ZWarden.Agent.Identity;
using ZWarden.Agent.Observability;
using ZWarden.Agent.Players;
using ZWarden.Agent.Rcon;
using ZWarden.Agent.SteamCmd;
using ZWarden.Agent.Trust;
using ZWarden.Rcon;

namespace ZWarden.Agent;

/// <summary>
/// Composition of the Agent runtime skeleton (F8): configuration, self-identity, health state,
/// diagnostics and the worker. Kept in one place so the wiring is auditable and the startup order
/// (identity resolved before the worker runs) is explicit.
/// </summary>
public static class HostingExtensions
{
    /// <summary>Registers the Agent runtime services and hosted lifecycle.</summary>
    public static IServiceCollection AddAgentRuntime(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

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

        // Docker runtime (F13): the client (constructed without an API-version override so it negotiates over
        // /_ping — ADR 0008), the mechanical engine over it, and the policy layer (ownership enforcement, the
        // closed create template, discovery, health). Correct with the socket unproxied.
        services.AddSingleton<IDockerClient>(sp =>
        {
            string? endpoint = sp.GetRequiredService<IOptions<AgentOptions>>().Value.DockerEndpoint;
            DockerClientBuilder builder = new();
            if (!string.IsNullOrWhiteSpace(endpoint))
            {
                builder = builder.WithEndpoint(new Uri(endpoint));
            }

            // No WithApiVersion(...): the client negotiates via /_ping and never pins a /v1.xx prefix (ADR 0008).
            return builder.Build();
        });
        services.AddSingleton<IDockerEngine, DockerDotNetEngine>();
        services.AddSingleton<ContainerOwnershipGuard>();
        services.AddSingleton<PzContainerFactory>();
        services.AddSingleton<IContainerRuntime, ContainerRuntime>();

        // Health and observability (F16): the pure rollup's inputs — the best-effort network probe and the
        // observer that inspects owned containers and evaluates their hierarchical health.
        services.AddSingleton<INetworkReachabilityProbe, UdpNetworkReachabilityProbe>();
        services.AddSingleton<IServerHealthObserver, ServerHealthObserver>();
        services.AddSingleton<IServerDiskUsageReader, ServerDiskUsageReader>();
        services.AddSingleton<IServerMetricsSampler, ServerMetricsSampler>();

        // SteamCMD lifecycle (F17): the host-side install paths and the update runner that drives an update via
        // the control-file + restart + log-parse loop (no exec — ADR 0008) and reads back the installed build id.
        services.AddSingleton<IServerInstallPaths, ServerInstallPaths>();
        services.AddSingleton<IServerUpdateRunner, ServerUpdateRunner>();

        // RCON foundation (F18): the Agent owns the RCON credential (seeded host-side into servertest.ini at
        // provision), resolves the container's private-network endpoint, and runs the on-demand health probe over
        // the ZWarden.Rcon client. The RCON password type lives only here in the Agent — never in Web (§9 rule 7).
        services.AddSingleton<IRconServerConfig, RconServerConfig>();
        services.AddSingleton<IRconConnectionFactory>(_ => new RconConnectionFactory());
        services.AddSingleton<IRconEndpointResolver, RconEndpointResolver>();
        services.AddSingleton<IRconHealthProbe, RconHealthProbe>();

        // Player management (F19): runs kick/ban/unban/remove-from-whitelist and the whitelist-mode toggle over
        // the Agent-owned RCON connection, building and quoting the admin commands (ADR 0026 deferred quoting to
        // F19) and parsing PZ's untrusted replies into typed results.
        services.AddSingleton<IPlayerAdministration, PlayerAdministration>();

        // Observability baseline (F16, ADR 0024): OpenTelemetry SDK + HttpClient instrumentation + opt-in OTLP.
        services.AddAgentTelemetry(configuration, environment);

        // Control plane (F10): the outbound SignalR connection the Agent opens once enrolled.
        services.AddSingleton<AgentCommandProcessor>();
        services.AddSingleton<IAgentControlPlaneConnection, SignalRControlPlaneConnection>();

        // Order matters: identity resolves, then enrollment runs, then the connection opens, before/while the
        // worker runs. Each hosted service's StartAsync completes before the next begins.
        services.AddHostedService<AgentIdentityInitializer>();
        services.AddHostedService<AgentEnrollmentInitializer>();
        services.AddHostedService<AgentConnectionInitializer>();
        // F16: reports server-health transitions between snapshots. Runs after the connection is opened; its sends
        // no-op while disconnected, so ordering is a convenience, not a correctness requirement.
        services.AddHostedService<ServerHealthMonitor>();
        services.AddHostedService<ServerMetricsMonitor>();
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
