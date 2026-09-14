using ZWarden.Agent.Rcon;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;
using ZWarden.Rcon;

namespace ZWarden.Agent.Players;

/// <summary>
/// Runs the F19 player-management actions for one Server over the Agent-owned RCON connection: resolve the
/// Server's private endpoint, build and quote the admin command (<see cref="PlayerCommandBuilder"/>), execute
/// it, and parse the untrusted reply (<see cref="PlayerResponseParser"/>) into a typed result. Any reason the
/// action could not run — invalid arguments, RCON disabled or unreachable, the password rejected, a timeout — is
/// raised as a <see cref="PlayerCommandException"/> with an Agent-authored, non-secret message; the command
/// processor maps that to a failed Operation. The connection is always disposed (cap-slot safety, research §7).
/// </summary>
public interface IPlayerAdministration
{
    Task<PlayerRosterResult> ListPlayersAsync(ServerId serverId, CancellationToken cancellationToken);

    Task<PlayerActionResult> KickAsync(ServerId serverId, string username, string? reason, CancellationToken cancellationToken);

    Task<PlayerActionResult> BanAsync(ServerId serverId, string username, string? reason, CancellationToken cancellationToken);

    Task<PlayerActionResult> UnbanAsync(ServerId serverId, string username, CancellationToken cancellationToken);

    Task<PlayerActionResult> RemoveFromWhitelistAsync(ServerId serverId, string username, CancellationToken cancellationToken);

    Task<PlayerActionResult> SetWhitelistModeAsync(ServerId serverId, bool open, CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class PlayerAdministration : IPlayerAdministration
{
    private readonly IRconEndpointResolver _resolver;
    private readonly IRconConnectionFactory _connections;

    public PlayerAdministration(IRconEndpointResolver resolver, IRconConnectionFactory connections)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(connections);
        _resolver = resolver;
        _connections = connections;
    }

    /// <inheritdoc />
    public async Task<PlayerRosterResult> ListPlayersAsync(ServerId serverId, CancellationToken cancellationToken)
    {
        string reply = await ExecuteAsync(serverId, PlayerCommandBuilder.List(), cancellationToken).ConfigureAwait(false);
        return PlayerResponseParser.ParseRoster(reply);
    }

    /// <inheritdoc />
    public async Task<PlayerActionResult> KickAsync(ServerId serverId, string username, string? reason, CancellationToken cancellationToken)
    {
        string command = PlayerCommandBuilder.Kick(username, reason);
        string reply = await ExecuteAsync(serverId, command, cancellationToken).ConfigureAwait(false);
        return PlayerResponseParser.ParseKick(reply);
    }

    /// <inheritdoc />
    public async Task<PlayerActionResult> BanAsync(ServerId serverId, string username, string? reason, CancellationToken cancellationToken)
    {
        string command = PlayerCommandBuilder.Ban(username, reason);
        string reply = await ExecuteAsync(serverId, command, cancellationToken).ConfigureAwait(false);
        return PlayerResponseParser.ParseProseAction(reply);
    }

    /// <inheritdoc />
    public async Task<PlayerActionResult> UnbanAsync(ServerId serverId, string username, CancellationToken cancellationToken)
    {
        string command = PlayerCommandBuilder.Unban(username);
        string reply = await ExecuteAsync(serverId, command, cancellationToken).ConfigureAwait(false);
        return PlayerResponseParser.ParseProseAction(reply);
    }

    /// <inheritdoc />
    public async Task<PlayerActionResult> RemoveFromWhitelistAsync(ServerId serverId, string username, CancellationToken cancellationToken)
    {
        string command = PlayerCommandBuilder.RemoveFromWhitelist(username);
        string reply = await ExecuteAsync(serverId, command, cancellationToken).ConfigureAwait(false);
        return PlayerResponseParser.ParseProseAction(reply);
    }

    /// <inheritdoc />
    public async Task<PlayerActionResult> SetWhitelistModeAsync(ServerId serverId, bool open, CancellationToken cancellationToken)
    {
        string command = PlayerCommandBuilder.SetWhitelistMode(open);
        string reply = await ExecuteAsync(serverId, command, cancellationToken).ConfigureAwait(false);
        return PlayerResponseParser.ParseWhitelistMode(reply);
    }

    private async Task<string> ExecuteAsync(ServerId serverId, string command, CancellationToken cancellationToken)
    {
        RconResolveResult resolution = await _resolver.ResolveAsync(serverId, cancellationToken).ConfigureAwait(false);
        switch (resolution.Status)
        {
            case RconResolveStatus.NoContainer:
                throw new PlayerCommandException(
                    "No running container on this host for the server, or it has no address on the ZWarden network.");

            case RconResolveStatus.RconDisabled:
                throw new PlayerCommandException(
                    "RCON is disabled: no password is set in the server configuration.");
        }

        RconEndpoint endpoint = resolution.Endpoint!.Value;
        IRconConnection connection = _connections.Create(endpoint);
        try
        {
            return await connection.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
        }
        catch (RconAuthenticationException)
        {
            throw new PlayerCommandException("The RCON password was rejected by the server.");
        }
        catch (RconTimeoutException)
        {
            throw new PlayerCommandException("Timed out running the command against the server's RCON port.");
        }
        catch (RconException)
        {
            throw new PlayerCommandException(
                "The server's RCON port could not be reached (refused, or the connection cap is full).");
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
