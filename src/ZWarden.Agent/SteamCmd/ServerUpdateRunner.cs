using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.ControlPlane;
using ZWarden.Agent.Docker;
using ZWarden.Agent.Servers;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.SteamCmd;

/// <summary>The result of a SteamCMD update the Agent drove and observed (F17).</summary>
/// <param name="Succeeded">Whether the entrypoint reported the update finished successfully.</param>
/// <param name="InstalledBuildId">The build id read from the manifest on success; <c>null</c> otherwise.</param>
/// <param name="FailureReason">On failure, an actionable (possibly untrusted) reason; <c>null</c> on success.</param>
public sealed record ServerUpdateOutcome(bool Succeeded, string? InstalledBuildId, string? FailureReason);

/// <summary>
/// Drives one SteamCMD update against a Server the Agent owns (F17), without <c>exec</c> (ADR 0008 denies it):
/// drop the update control-file into the data volume, restart the container so the entrypoint runs
/// <c>app_update … validate</c>, then poll the container log and translate SteamCMD's output into progress and a
/// terminal outcome (<see cref="SteamCmdLogParser"/>). Success reads the installed build id from the manifest.
/// </summary>
public interface IServerUpdateRunner
{
    /// <summary>Runs the update for <paramref name="serverId"/> under <paramref name="operationId"/>, reporting
    /// progress through <paramref name="progress"/>, and returns the terminal outcome.</summary>
    Task<ServerUpdateOutcome> RunAsync(
        ServerId serverId, OperationId operationId, IOperationProgressReporter progress, CancellationToken cancellationToken);
}

/// <inheritdoc cref="IServerUpdateRunner" />
public sealed partial class ServerUpdateRunner : IServerUpdateRunner
{
    private const int MaxReasonLength = 500;

    private readonly IContainerRuntime _runtime;
    private readonly IServerInstallPaths _paths;
    private readonly IServerRestartCoordinator _restartCoordinator;
    private readonly AgentOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ServerUpdateRunner> _logger;

    public ServerUpdateRunner(
        IContainerRuntime runtime,
        IServerInstallPaths paths,
        IServerRestartCoordinator restartCoordinator,
        IOptions<AgentOptions> options,
        TimeProvider timeProvider,
        ILogger<ServerUpdateRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(restartCoordinator);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _runtime = runtime;
        _paths = paths;
        _restartCoordinator = restartCoordinator;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ServerUpdateOutcome> RunAsync(
        ServerId serverId, OperationId operationId, IOperationProgressReporter progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);

        // The session id the entrypoint brackets its SteamCMD output with is the OperationId (F17 PR-A).
        string session = operationId.ToString();
        _paths.WriteUpdateRequest(serverId, operationId);

        try
        {
            // Graceful restart (#114): warn connected players with a servermsg countdown before the update takes
            // the server down. Best-effort — an RCON failure never blocks the update — using the Agent's default
            // warning schedule.
            await _restartCoordinator
                .WarnAsync(serverId, plan: null, operationId, progress, cancellationToken).ConfigureAwait(false);

            // The restart runs the blessed save→quit stop and reboots into the entrypoint's update path.
            await _runtime.RestartAsync(serverId, cancellationToken).ConfigureAwait(false);
        }
        catch (ContainerNotFoundException)
        {
            return new ServerUpdateOutcome(
                false, null, "This server has no container on its host to update. Provision (register) the server first.");
        }

        DateTimeOffset deadline = _timeProvider.GetUtcNow() + _options.UpdateTimeout;
        int lastReportedPercent = -1;

        while (true)
        {
            string log;
            try
            {
                log = await _runtime.ReadServerLogsAsync(serverId, since: null, cancellationToken).ConfigureAwait(false);
            }
            catch (ContainerNotFoundException)
            {
                return new ServerUpdateOutcome(false, null, "The server's container disappeared during the update.");
            }

            SteamCmdUpdateState state = SteamCmdLogParser.Parse(log, session);

            if (state.LatestProgress is { } current && current.Percent != lastReportedPercent)
            {
                lastReportedPercent = current.Percent;
                await progress.ReportAsync(operationId, current.Percent, current.Status, cancellationToken).ConfigureAwait(false);
            }

            switch (state.Outcome)
            {
                case SteamCmdOutcome.Succeeded:
                    string? buildId = _paths.ReadInstalledBuildId(serverId);
                    LogUpdateSucceeded(serverId, buildId ?? "unknown");
                    return new ServerUpdateOutcome(true, buildId, null);

                case SteamCmdOutcome.Failed:
                    LogUpdateFailed(serverId);
                    return new ServerUpdateOutcome(
                        false, null, Truncate(state.FailureReason) ?? "The SteamCMD update failed; see the server log.");
            }

            await Task.Delay(_options.UpdatePollInterval, cancellationToken).ConfigureAwait(false);

            if (_timeProvider.GetUtcNow() >= deadline)
            {
                LogUpdateTimedOut(serverId);
                return new ServerUpdateOutcome(false, null, "The SteamCMD update did not complete within the allotted time.");
            }
        }
    }

    private static string? Truncate(string? reason) =>
        reason is null ? null : reason.Length <= MaxReasonLength ? reason : reason[..MaxReasonLength];

    [LoggerMessage(Level = LogLevel.Information, Message = "SteamCMD update for server {ServerId} succeeded (build {BuildId}).")]
    private partial void LogUpdateSucceeded(ServerId serverId, string buildId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "SteamCMD update for server {ServerId} failed; the existing install stays in service.")]
    private partial void LogUpdateFailed(ServerId serverId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "SteamCMD update for server {ServerId} timed out; the operation's lease will fail it.")]
    private partial void LogUpdateTimedOut(ServerId serverId);
}
