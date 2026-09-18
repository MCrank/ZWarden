using Microsoft.AspNetCore.Authentication;
using ZWarden.Application.Agents;
using ZWarden.Application.Configuration;
using ZWarden.Contracts.Protocol;

namespace ZWarden.Web.Agents;

/// <summary>
/// Composition seam for the SignalR Agent control plane (F10). Registers the <c>Agent</c> authentication
/// scheme (which verifies the F9 per-Agent credential at the handshake), the in-memory connection registry
/// (a singleton — it holds live connections and is shared with F9's revoke/disable path), SignalR configured
/// with the canonical protocol serializer, and the connection-monitor sweep. Call it after
/// <c>AddZWardenEnrollment</c> (it consumes the credential verifier, the connection-state writer and the
/// sweeper registered there). The hub endpoint is mapped with <see cref="MapAgentHub"/>.
/// </summary>
public static class AgentControlPlaneServiceCollectionExtensions
{
    public static IServiceCollection AddAgentControlPlane(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The per-process registry of live connections: the seam F11 dispatches through and F9 revoke/disable
        // aborts through, so it must be the one shared singleton.
        services.AddSingleton<IAgentConnectionRegistry, AgentConnectionRegistry>();

        // The live-log viewer ref-count (F27): drives the owning Agent's on-demand follow on/off through the hub.
        // A singleton, beside the registry it routes through.
        services.AddSingleton<IServerLogSubscriptionCoordinator, ServerLogSubscriptionCoordinator>();

        // The live configuration read coordinator (F20c, ADR 0041): correlates a read request with its chunked
        // reply. A singleton — the AgentHub feeds reply chunks into the same instance ReadAsync awaits. It is the
        // IServerConfigReadChannel the Infrastructure reader (registered in AddZWardenConfiguration) depends on.
        services.AddSingleton<ServerConfigReadCoordinator>();
        services.AddSingleton<IServerConfigReadChannel>(sp => sp.GetRequiredService<ServerConfigReadCoordinator>());

        // The raw whole-file edit staging channel (F20c PR-D, ADR 0042): chunks the operator's text up the owning
        // Agent's connection ahead of the ConfigApplyRaw Operation. The IServerConfigRawEditChannel the
        // Infrastructure editor (AddZWardenConfiguration) depends on; a singleton beside the registry it routes through.
        services.AddSingleton<IServerConfigRawEditChannel, ServerConfigRawEditCoordinator>();

        // The bearer-credential handshake scheme. Added alongside the cookie schemes without changing the
        // application's default scheme; the hub opts into it explicitly via [Authorize].
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, AgentAuthenticationHandler>(
                AgentAuthenticationHandler.SchemeName, static _ => { });

        // Messages travel as Envelope<T> arguments serialized with the canonical protocol options (typed ids
        // as prefixed strings, enums by name) so both ends agree on the wire (ADR 0020).
        services.AddSignalR()
            .AddJsonProtocol(options => options.PayloadSerializerOptions = ProtocolJson.Options);

        // The connection monitor: reconciles stale/silent connections to disconnected (the sweeper itself and
        // its options are registered by AddZWardenEnrollment).
        services.AddHostedService<AgentConnectionSweeperService>();

        return services;
    }

    /// <summary>Maps the Agent hub at <see cref="AgentHubProtocol.Path"/>.</summary>
    public static IEndpointRouteBuilder MapAgentHub(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapHub<AgentHub>(AgentHubProtocol.Path);
        return endpoints;
    }
}
