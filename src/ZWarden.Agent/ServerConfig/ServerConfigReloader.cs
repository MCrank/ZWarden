using ZWarden.Agent.Rcon;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;
using ZWarden.Rcon;

namespace ZWarden.Agent.ServerConfig;

/// <summary>The outcome of a live reload attempt: what happened and, when it did not reload, why.</summary>
public sealed record ConfigReloadAttempt(ConfigReloadOutcome Outcome, string? Detail = null);

/// <summary>
/// Makes a just-written <c>servertest.ini</c> live on a running server by sending RCON <c>reloadoptions</c> (#225).
/// Verified on B42 42.20.4: PZ replies "Options reloaded", <c>showoptions</c> shows the new values, and PZ rewrites the
/// INI right away keeping them. Best-effort: it never throws for an RCON problem (a write that succeeded is never
/// rolled back) — it reports what happened so the operator knows whether the change is live or waits for a restart.
/// </summary>
public interface IServerConfigReloader
{
    Task<ConfigReloadAttempt> ReloadAsync(ServerId serverId, CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed partial class ServerConfigReloader : IServerConfigReloader
{
    /// <summary>The PZ console command that reloads the server options (the INI) and sends them to clients.</summary>
    public const string ReloadCommand = "reloadoptions";

    // PZ's success reply (ReloadOptionsCommand).
    private const string ReloadedReply = "Options reloaded";

    // The most of PZ's (untrusted) reply carried back as a failure detail.
    private const int MaxReplyLength = 200;

    private readonly IRconEndpointResolver _resolver;
    private readonly IRconConnectionFactory _connections;
    private readonly ILogger<ServerConfigReloader> _logger;

    public ServerConfigReloader(
        IRconEndpointResolver resolver, IRconConnectionFactory connections, ILogger<ServerConfigReloader> logger)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(logger);
        _resolver = resolver;
        _connections = connections;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ConfigReloadAttempt> ReloadAsync(ServerId serverId, CancellationToken cancellationToken)
    {
        RconResolveResult resolution;
        try
        {
            resolution = await _resolver.ResolveAsync(serverId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogReloadFailed(serverId, ex.Message);
            return new ConfigReloadAttempt(ConfigReloadOutcome.Failed, "RCON could not be resolved.");
        }

        switch (resolution.Status)
        {
            case RconResolveStatus.NoContainer:
                // Not running: nothing to reload — the next start reads the file (verified, #225).
                return new ConfigReloadAttempt(ConfigReloadOutcome.NotRunning);
            case RconResolveStatus.RconDisabled:
                return new ConfigReloadAttempt(ConfigReloadOutcome.Failed, "RCON is disabled on this server.");
            default:
                break;
        }

        IRconConnection connection = _connections.Create(resolution.Endpoint!.Value);
        try
        {
            string reply = await connection.ExecuteAsync(ReloadCommand, cancellationToken).ConfigureAwait(false);
            if (reply.Contains(ReloadedReply, StringComparison.OrdinalIgnoreCase))
            {
                return new ConfigReloadAttempt(ConfigReloadOutcome.Reloaded);
            }

            string trimmed = reply.Trim();
            string detail = trimmed.Length == 0
                ? "The server gave no reply to reloadoptions."
                : $"The server replied: {(trimmed.Length <= MaxReplyLength ? trimmed : trimmed[..MaxReplyLength])}";
            LogReloadFailed(serverId, detail);
            return new ConfigReloadAttempt(ConfigReloadOutcome.Failed, detail);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (RconException ex)
        {
            LogReloadFailed(serverId, ex.Message);
            return new ConfigReloadAttempt(ConfigReloadOutcome.Failed, ex.Message);
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Live config reload for server {ServerId} did not succeed: {Reason}")]
    private partial void LogReloadFailed(ServerId serverId, string reason);
}
