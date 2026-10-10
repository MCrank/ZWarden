using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.Docker;
using ZWarden.Agent.Rcon;
using ZWarden.Domain.Ids;
using ZWarden.Rcon;

namespace ZWarden.Agent.Backups;

/// <summary>What happened when the Agent asked a Server to save its world before a backup (#377).</summary>
public enum WorldSaveStatus
{
    /// <summary>PZ took the <c>save</c> command and the settle was waited.</summary>
    Saved,

    /// <summary>The Server's container isn't running, so its world is already on disk and needs no save.</summary>
    NotRunning,

    /// <summary>The Server is running but the save couldn't be sent; the reason says why.</summary>
    Failed,
}

/// <summary>The outcome of <see cref="IWorldSaver.SaveAsync"/>.</summary>
/// <param name="Status">Whether the world was saved, needed no save, or could not be saved.</param>
/// <param name="FailureReason">On <see cref="WorldSaveStatus.Failed"/>, an Agent-authored, non-secret reason;
/// <c>null</c> otherwise.</param>
public sealed record WorldSaveResult(WorldSaveStatus Status, string? FailureReason)
{
    public static WorldSaveResult Saved { get; } = new(WorldSaveStatus.Saved, null);

    public static WorldSaveResult NotRunning { get; } = new(WorldSaveStatus.NotRunning, null);

    public static WorldSaveResult Failed(string reason) => new(WorldSaveStatus.Failed, reason);
}

/// <summary>
/// Asks a running Server to save its world over RCON before a backup archives it (#377). Whether the Server is
/// running comes from its managed container's state (the Agent's ground truth, as restore uses), never from RCON.
/// PZ replies to <c>save</c> once the save is <i>queued</i>, so the saver then waits a fixed settle
/// (<see cref="AgentOptions.BackupSaveSettle"/>) for the game thread to write it. This is an Agent-authored
/// command, not operator input, so it goes straight to the RCON client rather than through the console policy.
/// A failure is returned, never thrown, so the backup can still run and say the world wasn't saved first.
/// </summary>
public interface IWorldSaver
{
    Task<WorldSaveResult> SaveAsync(ServerId serverId, CancellationToken cancellationToken);
}

/// <inheritdoc cref="IWorldSaver" />
public sealed partial class WorldSaver : IWorldSaver
{
    private readonly IContainerRuntime _runtime;
    private readonly IRconEndpointResolver _resolver;
    private readonly IRconConnectionFactory _connections;
    private readonly AgentOptions _options;
    private readonly ILogger<WorldSaver> _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public WorldSaver(
        IContainerRuntime runtime,
        IRconEndpointResolver resolver,
        IRconConnectionFactory connections,
        IOptions<AgentOptions> options,
        ILogger<WorldSaver> logger)
        : this(runtime, resolver, connections, options, logger, static (delay, ct) => Task.Delay(delay, ct))
    {
    }

    // Test seam: an injectable delay so the settle runs instantly and is observable.
    internal WorldSaver(
        IContainerRuntime runtime,
        IRconEndpointResolver resolver,
        IRconConnectionFactory connections,
        IOptions<AgentOptions> options,
        ILogger<WorldSaver> logger,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(delay);
        _runtime = runtime;
        _resolver = resolver;
        _connections = connections;
        _options = options.Value;
        _logger = logger;
        _delay = delay;
    }

    /// <inheritdoc />
    public async Task<WorldSaveResult> SaveAsync(ServerId serverId, CancellationToken cancellationToken)
    {
        IReadOnlyList<ManagedContainer> managed = await _runtime.ListManagedAsync(cancellationToken).ConfigureAwait(false);
        bool running = managed.Any(container =>
            container.ServerId == serverId
            && container.State.Equals("running", StringComparison.OrdinalIgnoreCase));
        if (!running)
        {
            return WorldSaveResult.NotRunning;
        }

        RconResolveResult resolution = await _resolver.ResolveAsync(serverId, cancellationToken).ConfigureAwait(false);
        switch (resolution.Status)
        {
            case RconResolveStatus.NoContainer:
                return Fail(serverId, "the server's RCON port could not be reached (it has no address on the ZWarden network)");

            case RconResolveStatus.RconDisabled:
                return Fail(serverId, "RCON is disabled: no password is set in the server configuration");
        }

        IRconConnection connection = _connections.Create(resolution.Endpoint!.Value);
        try
        {
            await connection.ExecuteAsync("save", cancellationToken).ConfigureAwait(false);
        }
        catch (RconAuthenticationException)
        {
            return Fail(serverId, "the RCON password was rejected by the server");
        }
        catch (RconTimeoutException)
        {
            return Fail(serverId, "the server's RCON port timed out");
        }
        catch (RconException)
        {
            return Fail(serverId, "the server's RCON port could not be reached");
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }

        // PZ has only queued the save; give the game thread time to write it before the archive reads the files.
        await _delay(_options.BackupSaveSettle, cancellationToken).ConfigureAwait(false);
        LogSaved(serverId);
        return WorldSaveResult.Saved;
    }

    private WorldSaveResult Fail(ServerId serverId, string reason)
    {
        LogSaveFailed(serverId, reason);
        return WorldSaveResult.Failed(reason);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Saved the world of server {ServerId} before its backup.")]
    private partial void LogSaved(ServerId serverId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not save the world of server {ServerId} before its backup: {Reason}.")]
    private partial void LogSaveFailed(ServerId serverId, string reason);
}
