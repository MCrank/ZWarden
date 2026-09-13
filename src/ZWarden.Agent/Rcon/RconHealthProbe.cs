using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;
using ZWarden.Rcon;

namespace ZWarden.Agent.Rcon;

/// <summary>
/// Runs the F18 RCON health probe for one Server: resolve its private endpoint, open the Agent-owned RCON
/// connection, authenticate, and classify the outcome into a <see cref="RconHealthResult"/> with a legible,
/// Agent-authored detail. Connect-and-authenticate is enough to prove RCON is healthy - it needs no command,
/// keeping load off PZ's tick-serialized, five-slot-capped listener (research §7). Every failure is a distinct,
/// reported case, never an opaque error or a hang: "no packet" and "closed socket" are already disentangled
/// inside <see cref="RconConnection"/>.
/// </summary>
public interface IRconHealthProbe
{
    Task<RconHealthResult> ProbeAsync(ServerId serverId, CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class RconHealthProbe : IRconHealthProbe
{
    private readonly IRconEndpointResolver _resolver;
    private readonly IRconConnectionFactory _connections;

    public RconHealthProbe(IRconEndpointResolver resolver, IRconConnectionFactory connections)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(connections);
        _resolver = resolver;
        _connections = connections;
    }

    /// <inheritdoc />
    public async Task<RconHealthResult> ProbeAsync(ServerId serverId, CancellationToken cancellationToken)
    {
        RconResolveResult resolution = await _resolver.ResolveAsync(serverId, cancellationToken).ConfigureAwait(false);
        switch (resolution.Status)
        {
            case RconResolveStatus.NoContainer:
                return new RconHealthResult(
                    Reachable: false,
                    Authenticated: false,
                    "No running container on this host for the server, or it has no address on the ZWarden network.");

            case RconResolveStatus.RconDisabled:
                return new RconHealthResult(
                    Reachable: false,
                    Authenticated: false,
                    "RCON is disabled: no password is set in the server configuration.");
        }

        RconEndpoint endpoint = resolution.Endpoint!.Value;
        IRconConnection connection = _connections.Create(endpoint);
        try
        {
            await connection.ConnectAsync(cancellationToken).ConfigureAwait(false);
            return new RconHealthResult(Reachable: true, Authenticated: true, Detail: null);
        }
        catch (RconAuthenticationException)
        {
            // TCP reached the listener, but the Agent-owned password was rejected (or the listener closed the
            // handshake). Reachable, not authenticated.
            return new RconHealthResult(
                Reachable: true,
                Authenticated: false,
                "The RCON password was rejected by the server.");
        }
        catch (RconTimeoutException)
        {
            return new RconHealthResult(
                Reachable: false,
                Authenticated: false,
                "Timed out connecting to the server's RCON port.");
        }
        catch (RconException)
        {
            // Connection refused, or the five-connection cap is full (the sixth connection EOFs at once).
            return new RconHealthResult(
                Reachable: false,
                Authenticated: false,
                "The server's RCON port could not be reached (refused, or the connection cap is full).");
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
