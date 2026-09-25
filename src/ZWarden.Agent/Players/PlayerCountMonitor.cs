using System.IO;
using System.Net.Http;
using Docker.DotNet;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.Docker;
using ZWarden.Agent.Trust;

namespace ZWarden.Agent.Players;

/// <summary>
/// Drives the <see cref="PlayerCountSampler"/> (#257). On the metrics cadence it lists the owned containers and lets
/// the sampler query whichever Running servers are due; the sampler owns the slower per-server RCON cadence. It
/// runs apart from the metrics monitor so a slow RCON reply never delays a CPU/memory report. It does nothing when
/// un-enrolled, and a Docker hiccup on one cycle is skipped, not fatal.
/// </summary>
public sealed partial class PlayerCountMonitor : BackgroundService
{
    private readonly IAgentTrustStore _trustStore;
    private readonly IContainerRuntime _runtime;
    private readonly PlayerCountSampler _sampler;
    private readonly AgentOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PlayerCountMonitor> _logger;

    public PlayerCountMonitor(
        IAgentTrustStore trustStore,
        IContainerRuntime runtime,
        PlayerCountSampler sampler,
        IOptions<AgentOptions> options,
        TimeProvider timeProvider,
        ILogger<PlayerCountMonitor> logger)
    {
        ArgumentNullException.ThrowIfNull(trustStore);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(sampler);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _trustStore = trustStore;
        _runtime = runtime;
        _sampler = sampler;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (await _trustStore.TryLoadAsync(stoppingToken).ConfigureAwait(false) is null)
        {
            LogNotSamplingUnenrolled();
            return;
        }

        try
        {
            using PeriodicTimer timer = new(_options.MetricsReportInterval, _timeProvider);
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await SweepAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ManagedContainer> managed;
        try
        {
            managed = await _runtime.ListManagedAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DockerApiException or HttpRequestException or IOException or TimeoutException)
        {
            LogSweepFailed(ex.Message);
            return;
        }

        await _sampler.SampleDueAsync(managed, cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent is un-enrolled; not sampling player counts.")]
    private partial void LogNotSamplingUnenrolled();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Player-count sweep skipped this cycle: {Detail}")]
    private partial void LogSweepFailed(string detail);
}
