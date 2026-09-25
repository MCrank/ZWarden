using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.ControlPlane;
using ZWarden.Agent.Docker;
using ZWarden.Agent.Players;
using ZWarden.Agent.Rcon;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;
using ZWarden.Rcon;

namespace ZWarden.Agent.Servers;

/// <summary>
/// Coordinates a <b>graceful restart</b> (#114): before a restart-causing Operation takes a Server down, broadcast
/// a <c>servermsg</c> countdown to connected players over the Agent-owned RCON connection (ADR 0026), then proceed
/// with the F15 safe stop→start. The broadcast is <b>best-effort and never blocks the restart</b> — if RCON is
/// unreachable, disabled, or the send fails, the warning is skipped/logged and the restart still happens. Cancelling
/// the Operation during the countdown (F11) aborts before any stop. It is the one place three restart paths meet:
/// the F15 <c>RestartServer</c> command calls <see cref="RestartAsync"/>; F17's SteamCMD update calls
/// <see cref="WarnAsync"/> before its own restart; F22 mod-apply inherits it through F15.
/// </summary>
public interface IServerRestartCoordinator
{
    /// <summary>Broadcasts the graceful-restart countdown for the Server, then returns — the caller performs the
    /// actual stop/restart. Best-effort: never throws on an RCON failure; only a cancellation propagates.</summary>
    Task WarnAsync(
        ServerId serverId,
        GracefulRestartPlan? plan,
        OperationId operationId,
        IOperationProgressReporter progress,
        CancellationToken cancellationToken);

    /// <summary>Runs the full graceful restart: <see cref="WarnAsync"/> then the F15 safe restart
    /// (<see cref="IContainerRuntime.RestartAsync(ServerId, CancellationToken)"/>). The restart half is not
    /// best-effort — a Docker fault there is a real failure the caller surfaces.</summary>
    Task RestartAsync(
        ServerId serverId,
        GracefulRestartPlan? plan,
        OperationId operationId,
        IOperationProgressReporter progress,
        CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed partial class ServerRestartCoordinator : IServerRestartCoordinator
{
    private readonly IRconEndpointResolver _resolver;
    private readonly IRconConnectionFactory _connections;
    private readonly IContainerRuntime _runtime;
    private readonly AgentOptions _options;
    private readonly ILogger<ServerRestartCoordinator> _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public ServerRestartCoordinator(
        IRconEndpointResolver resolver,
        IRconConnectionFactory connections,
        IContainerRuntime runtime,
        IOptions<AgentOptions> options,
        ILogger<ServerRestartCoordinator> logger)
        : this(resolver, connections, runtime, options, logger, static (delay, ct) => Task.Delay(delay, ct))
    {
    }

    // Test seam: an injectable delay so a multi-minute countdown runs instantly and deterministically.
    internal ServerRestartCoordinator(
        IRconEndpointResolver resolver,
        IRconConnectionFactory connections,
        IContainerRuntime runtime,
        IOptions<AgentOptions> options,
        ILogger<ServerRestartCoordinator> logger,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(delay);
        _resolver = resolver;
        _connections = connections;
        _runtime = runtime;
        _options = options.Value;
        _logger = logger;
        _delay = delay;
    }

    /// <inheritdoc />
    public async Task RestartAsync(
        ServerId serverId,
        GracefulRestartPlan? plan,
        OperationId operationId,
        IOperationProgressReporter progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        await WarnAsync(serverId, plan, operationId, progress, cancellationToken).ConfigureAwait(false);
        await _runtime.RestartAsync(serverId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task WarnAsync(
        ServerId serverId,
        GracefulRestartPlan? plan,
        OperationId operationId,
        IOperationProgressReporter progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);

        IReadOnlyList<int> leads = ResolveSchedule(serverId, plan);
        if (leads.Count == 0)
        {
            return; // No countdown configured, or an explicit skip — restart immediately.
        }

        string? reason = ResolveReason(plan);

        // Resolve the endpoint once. If RCON is unavailable there is nobody to warn and no reason to wait, so skip
        // the whole countdown rather than block the restart (#114 — best-effort, never blocks).
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
            LogBroadcastSkipped(serverId, ex.Message);
            await ReportAsync(progress, operationId, "Player warning skipped: RCON could not be resolved.", cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (resolution.Status != RconResolveStatus.Resolved)
        {
            LogBroadcastUnavailable(serverId, resolution.Status);
            await ReportAsync(progress, operationId, "Player warning skipped: RCON is unavailable.", cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        RconEndpoint endpoint = resolution.Endpoint!.Value;

        // #254: the Agent-default countdown (no plan — the header Restart, an update, a mod apply) has nobody to
        // warn on an empty server, so it would only delay the restart. An explicit plan (the countdown panel) is the
        // operator asking for the warning, so it always runs.
        if (plan is null && await NobodyOnlineAsync(endpoint, cancellationToken).ConfigureAwait(false))
        {
            LogCountdownSkippedEmpty(serverId);
            await ReportAsync(progress, operationId, "No players online; restarting without a countdown.", cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        for (int i = 0; i < leads.Count; i++)
        {
            int lead = leads[i];
            await BroadcastBestEffortAsync(endpoint, serverId, lead, reason, operationId, progress, cancellationToken)
                .ConfigureAwait(false);

            int nextLead = i + 1 < leads.Count ? leads[i + 1] : 0;
            await CountdownDelayAsync(lead - nextLead, nextLead, operationId, progress, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    // True only when PZ's roster reply positively says nobody is connected. Fails safe: a failed query or an
    // unrecognised reply keeps the countdown, since skipping it with players online would give them no warning.
    private async Task<bool> NobodyOnlineAsync(RconEndpoint endpoint, CancellationToken cancellationToken)
    {
        IRconConnection connection = _connections.Create(endpoint);
        try
        {
            string reply = await connection.ExecuteAsync("players", cancellationToken).ConfigureAwait(false);
            return PlayerResponseParser.TryParseConnectedCount(reply, out int count) && count == 0;
        }
        catch (RconException)
        {
            return false;
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    // The effective schedule: the command's plan when present, otherwise the Agent default. An invalid schedule
    // (which the Web edge and options validation should already have refused) fails safe to "skip the broadcast".
    private IReadOnlyList<int> ResolveSchedule(ServerId serverId, GracefulRestartPlan? plan)
    {
        IReadOnlyList<int> leads = plan?.WarningLeadSeconds ?? _options.RestartWarningLeadSeconds;
        if (GracefulRestartRules.ValidateSchedule(leads) is { } error)
        {
            LogInvalidSchedule(serverId, error);
            return [];
        }

        return leads;
    }

    // The effective reason: the command's reason when present, otherwise the Agent default. An invalid reason is
    // dropped rather than allowed to fail the (best-effort) broadcast.
    private string? ResolveReason(GracefulRestartPlan? plan)
    {
        string? reason = plan?.Reason ?? _options.RestartWarningReason;
        return reason is not null && GracefulRestartRules.ValidateReason(reason) is not null ? null : reason;
    }

    private async Task BroadcastBestEffortAsync(
        RconEndpoint endpoint,
        ServerId serverId,
        int lead,
        string? reason,
        OperationId operationId,
        IOperationProgressReporter progress,
        CancellationToken cancellationToken)
    {
        string command;
        try
        {
            command = ServerBroadcastCommandBuilder.ServerMessage(BroadcastMessageRules.FormatCountdown(lead, reason));
        }
        catch (BroadcastCommandException ex)
        {
            // Defensive — the countdown text is Agent-composed and always valid, so this should never fire.
            LogBroadcastSkipped(serverId, ex.Message);
            return;
        }

        IRconConnection connection = _connections.Create(endpoint);
        try
        {
            await connection.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
            await ReportAsync(progress, operationId, $"Warned players: restarting in {lead} seconds.", cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (RconException ex)
        {
            // Best-effort: a failed broadcast never blocks the restart (#114).
            LogBroadcastFailed(serverId, ex.Message);
            await ReportAsync(progress, operationId, "A player warning could not be delivered; the restart continues.", cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    // Waits out the gap between two broadcasts in heartbeat-sized chunks, reporting progress on each so the F11
    // operation lease (5 min, extended by every progress report — ADR 0022) stays alive through a long gap.
    private async Task CountdownDelayAsync(
        int seconds,
        int remainingLeadAfter,
        OperationId operationId,
        IOperationProgressReporter progress,
        CancellationToken cancellationToken)
    {
        if (seconds <= 0)
        {
            return;
        }

        int heartbeat = Math.Max(1, (int)_options.HeartbeatInterval.TotalSeconds);
        int elapsed = 0;
        while (elapsed < seconds)
        {
            int chunk = Math.Min(heartbeat, seconds - elapsed);
            await _delay(TimeSpan.FromSeconds(chunk), cancellationToken).ConfigureAwait(false);
            elapsed += chunk;

            int remaining = remainingLeadAfter + (seconds - elapsed);
            await ReportAsync(progress, operationId, $"Restarting in {remaining} seconds.", cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task ReportAsync(
        IOperationProgressReporter progress,
        OperationId operationId,
        string statusLine,
        CancellationToken cancellationToken)
        => await progress.ReportAsync(operationId, 0, statusLine, cancellationToken).ConfigureAwait(false);

    [LoggerMessage(Level = LogLevel.Information, Message = "Graceful-restart broadcast skipped for server {ServerId}: {Reason}")]
    private partial void LogBroadcastSkipped(ServerId serverId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Graceful-restart broadcast skipped for server {ServerId}: RCON {Status}")]
    private partial void LogBroadcastUnavailable(ServerId serverId, RconResolveStatus status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A graceful-restart broadcast to server {ServerId} failed; the restart continues: {Reason}")]
    private partial void LogBroadcastFailed(ServerId serverId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "No players online on server {ServerId}; restarting without the default countdown.")]
    private partial void LogCountdownSkippedEmpty(ServerId serverId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignoring an invalid graceful-restart schedule for server {ServerId}: {Reason}")]
    private partial void LogInvalidSchedule(ServerId serverId, string reason);
}
