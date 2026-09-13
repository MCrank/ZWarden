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

    public Task<EngineContainer> InspectAsync(string containerId, CancellationToken cancellationToken) =>
        Task.FromResult(InspectResult ?? throw new InvalidOperationException("No inspect result arranged."));

    public ContainerStatsSnapshot StatsResult { get; set; }

    public Task<ContainerStatsSnapshot> StatsAsync(string containerId, CancellationToken cancellationToken) =>
        Task.FromResult(StatsResult);

    /// <summary>The log text <see cref="ReadLogsAsync"/> returns.</summary>
    public string LogText { get; set; } = string.Empty;

    /// <summary>The <c>since</c> the last <see cref="ReadLogsAsync"/> was asked for.</summary>
    public DateTimeOffset? LastLogsSince { get; private set; }

    public Task<string> ReadLogsAsync(string containerId, DateTimeOffset? since, CancellationToken cancellationToken)
    {
        LastLogsSince = since;
        return Task.FromResult(LogText);
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
}
