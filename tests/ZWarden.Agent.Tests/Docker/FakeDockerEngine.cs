using Docker.DotNet.Models;
using ZWarden.Agent.Docker;

namespace ZWarden.Agent.Tests.Docker;

/// <summary>
/// An in-memory <see cref="IDockerEngine"/> for unit-testing <see cref="ContainerRuntime"/> without a live
/// daemon: it records what it was asked to do and returns (or throws) whatever a test arranges.
/// </summary>
internal sealed class FakeDockerEngine : IDockerEngine
{
    public string ApiVersion { get; set; } = "1.53";

    public Exception? PingException { get; set; }

    public List<EngineContainer> Listed { get; } = [];

    public EngineContainer? InspectResult { get; set; }

    public Exception? CreateException { get; set; }

    public CreateContainerParameters? CreatedWith { get; private set; }

    public string CreatedId { get; set; } = "new-container-id";

    public Exception? VerbException { get; set; }

    public List<string> Started { get; } = [];

    public List<string> Stopped { get; } = [];

    public List<string> Restarted { get; } = [];

    /// <summary>The <c>WaitBeforeKillSeconds</c> the last stop/restart was issued with (F15 safe-stop timeout).</summary>
    public int? LastWaitBeforeKillSeconds { get; private set; }

    public Task<string> PingApiVersionAsync(CancellationToken cancellationToken) =>
        PingException is not null ? throw PingException : Task.FromResult(ApiVersion);

    public Task<IReadOnlyList<EngineContainer>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EngineContainer>>(Listed);

    /// <summary>Per-container inspect results; falls back to <see cref="InspectResult"/> for an unlisted id.</summary>
    public Dictionary<string, EngineContainer> Inspected { get; } = new(StringComparer.Ordinal);

    /// <summary>Thrown by <see cref="InspectAsync"/> when set (a container vanishing between list and inspect).</summary>
    public Exception? InspectException { get; set; }

    public Task<EngineContainer> InspectAsync(string containerId, CancellationToken cancellationToken) =>
        InspectException is not null
            ? Task.FromException<EngineContainer>(InspectException)
            : Task.FromResult(Inspected.TryGetValue(containerId, out EngineContainer? inspected)
                ? inspected
                : InspectResult ?? throw new InvalidOperationException("No inspect result arranged."));

    /// <summary>What <see cref="TotalMemoryBytesAsync"/> returns (#230).</summary>
    public long TotalMemoryBytes { get; set; }

    public Task<long> TotalMemoryBytesAsync(CancellationToken cancellationToken) => Task.FromResult(TotalMemoryBytes);

    public ContainerStatsSnapshot StatsResult { get; set; }

    public Task<ContainerStatsSnapshot> StatsAsync(string containerId, CancellationToken cancellationToken) =>
        Task.FromResult(StatsResult);

    /// <summary>The log text <see cref="ReadLogsAsync"/> returns.</summary>
    public string LogText { get; set; } = string.Empty;

    /// <summary>Thrown by <see cref="ReadLogsAsync"/> when set.</summary>
    public Exception? LogsException { get; set; }

    /// <summary>The <c>since</c> the last <see cref="ReadLogsAsync"/> was asked for.</summary>
    public DateTimeOffset? LastLogsSince { get; private set; }

    /// <summary>The <c>until</c> the last <see cref="ReadLogsAsync"/> was asked for.</summary>
    public DateTimeOffset? LastLogsUntil { get; private set; }

    /// <summary>How many times <see cref="ReadLogsAsync"/> was called.</summary>
    public int ReadLogsCount { get; private set; }

    public Task<string> ReadLogsAsync(string containerId, DateTimeOffset? since, DateTimeOffset? until, CancellationToken cancellationToken)
    {
        ReadLogsCount++;
        LastLogsSince = since;
        LastLogsUntil = until;
        if (LogsException is not null)
        {
            return Task.FromException<string>(LogsException);
        }

        return Task.FromResult(LogText);
    }

    /// <summary>The frames <see cref="FollowLogsAsync"/> replays through its callback before (optionally) blocking.</summary>
    public List<ContainerLogFrame> LogFrames { get; } = [];

    /// <summary>The container id the last <see cref="FollowLogsAsync"/> was asked to follow.</summary>
    public string? LastFollowContainerId { get; private set; }

    /// <summary>The <c>tailLines</c> the last <see cref="FollowLogsAsync"/> was asked for.</summary>
    public int? LastFollowTailLines { get; private set; }

    /// <summary>When set, <see cref="FollowLogsAsync"/> blocks after replaying frames until the token cancels —
    /// simulating a live, long-lived stream so a subscription's teardown can be exercised.</summary>
    public bool FollowBlocksUntilCancelled { get; set; }

    public async Task FollowLogsAsync(
        string containerId,
        int tailLines,
        Func<ContainerLogFrame, CancellationToken, ValueTask> onFrame,
        CancellationToken cancellationToken)
    {
        LastFollowContainerId = containerId;
        LastFollowTailLines = tailLines;
        foreach (ContainerLogFrame frame in LogFrames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await onFrame(frame, cancellationToken).ConfigureAwait(false);
        }

        if (FollowBlocksUntilCancelled)
        {
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The subscription was torn down — the expected end of a live follow.
            }
        }
    }

    public Task<string> CreateAsync(CreateContainerParameters parameters, CancellationToken cancellationToken)
    {
        CreatedWith = parameters;
        return CreateException is not null ? throw CreateException : Task.FromResult(CreatedId);
    }

    public Task StartAsync(string containerId, CancellationToken cancellationToken)
    {
        if (VerbException is not null)
        {
            throw VerbException;
        }

        Started.Add(containerId);
        return Task.CompletedTask;
    }

    public Task StopAsync(string containerId, int waitBeforeKillSeconds, CancellationToken cancellationToken)
    {
        if (VerbException is not null)
        {
            throw VerbException;
        }

        LastWaitBeforeKillSeconds = waitBeforeKillSeconds;
        Stopped.Add(containerId);
        return Task.CompletedTask;
    }

    public Task RestartAsync(string containerId, int waitBeforeKillSeconds, CancellationToken cancellationToken)
    {
        if (VerbException is not null)
        {
            throw VerbException;
        }

        LastWaitBeforeKillSeconds = waitBeforeKillSeconds;
        Restarted.Add(containerId);
        return Task.CompletedTask;
    }

    public List<string> Removed { get; } = [];

    public Task RemoveAsync(string containerName, CancellationToken cancellationToken)
    {
        if (VerbException is not null)
        {
            throw VerbException;
        }

        Removed.Add(containerName);
        return Task.CompletedTask;
    }
}
