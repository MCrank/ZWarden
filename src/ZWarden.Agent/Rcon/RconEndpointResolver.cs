using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.Docker;
using ZWarden.Domain.Ids;
using ZWarden.Rcon;

namespace ZWarden.Agent.Rcon;

/// <summary>Why an RCON endpoint could - or could not - be resolved for a Server, kept distinct so the health
/// probe can report a legible reason rather than a bare failure.</summary>
public enum RconResolveStatus
{
    /// <summary>The container was found on the ZWarden network and a password is configured.</summary>
    Resolved,

    /// <summary>No container this Agent owns for the Server, or it has no address on the ZWarden network (e.g.
    /// it is not running).</summary>
    NoContainer,

    /// <summary>The container is present but RCON is disabled - no password in <c>servertest.ini</c>.</summary>
    RconDisabled,
}

/// <summary>The outcome of resolving a Server's RCON endpoint. <see cref="Endpoint"/> is set only when
/// <see cref="Status"/> is <see cref="RconResolveStatus.Resolved"/>.</summary>
public readonly record struct RconResolveResult(RconResolveStatus Status, RconEndpoint? Endpoint)
{
    public static RconResolveResult NoContainer { get; } = new(RconResolveStatus.NoContainer, null);
    public static RconResolveResult RconDisabled { get; } = new(RconResolveStatus.RconDisabled, null);
    public static RconResolveResult Resolved(RconEndpoint endpoint) => new(RconResolveStatus.Resolved, endpoint);
}

/// <summary>
/// Resolves the private-network <see cref="RconEndpoint"/> for a Server: the container's IP on the ZWarden
/// network (from Docker inspect - the already-allowlisted <c>GET /containers/{id}/json</c>, no new verb) paired
/// with the Agent-owned password read from <c>servertest.ini</c>. RCON is never host-published, so this bridge
/// address is the only way to reach it.
/// </summary>
public interface IRconEndpointResolver
{
    Task<RconResolveResult> ResolveAsync(ServerId serverId, CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class RconEndpointResolver : IRconEndpointResolver
{
    private readonly IContainerRuntime _runtime;
    private readonly IRconServerConfig _config;
    private readonly AgentOptions _options;

    public RconEndpointResolver(IContainerRuntime runtime, IRconServerConfig config, IOptions<AgentOptions> options)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(options);
        _runtime = runtime;
        _config = config;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<RconResolveResult> ResolveAsync(ServerId serverId, CancellationToken cancellationToken)
    {
        string? address = await _runtime
            .ResolveNetworkAddressAsync(serverId, _options.NetworkName, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrEmpty(address))
        {
            return RconResolveResult.NoContainer;
        }

        if (_config.ReadPassword(serverId) is not { } password)
        {
            return RconResolveResult.RconDisabled;
        }

        return RconResolveResult.Resolved(new RconEndpoint(address, RconServerConfig.RconPort, password));
    }
}
