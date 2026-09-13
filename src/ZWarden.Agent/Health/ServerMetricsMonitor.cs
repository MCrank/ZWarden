using System.IO;
using System.Net.Http;
using Docker.DotNet;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.ControlPlane;
using ZWarden.Agent.Trust;
using ZWarden.Contracts.Protocol.Messages;

namespace ZWarden.Agent.Health;

/// <summary>
/// The Agent's periodic runtime-metrics reporter (F16). On <see cref="AgentOptions.MetricsReportInterval"/> it
/// samples every owned Server (<see cref="IServerMetricsSampler"/>) and pushes the latest snapshot as a
/// <c>ServerMetricsReport</c>. Unlike health (which reports only transitions), metrics change every sample, so
/// each cycle sends the full latest set — ZWarden.Web keeps only the newest per Server. It does nothing when
/// un-enrolled, and a Docker hiccup on one cycle is skipped, not fatal.
/// </summary>
public sealed partial class ServerMetricsMonitor : BackgroundService
{
    private readonly IAgentTrustStore _trustStore;
    private readonly IServerMetricsSampler _sampler;
    private readonly IAgentControlPlaneConnection _connection;
    private readonly AgentOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ServerMetricsMonitor> _logger;

    public ServerMetricsMonitor(
        IAgentTrustStore trustStore,
        IServerMetricsSampler sampler,
        IAgentControlPlaneConnection connection,
        IOptions<AgentOptions> options,
        TimeProvider timeProvider,
        ILogger<ServerMetricsMonitor> logger)
    {
        ArgumentNullException.ThrowIfNull(trustStore);
        ArgumentNullException.ThrowIfNull(sampler);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _trustStore = trustStore;
        _sampler = sampler;
        _connection = connection;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        AgentTrustMaterial? material = await _trustStore.TryLoadAsync(stoppingToken).ConfigureAwait(false);
        if (material is null)
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
        IReadOnlyList<ServerMetricsSample> samples;
        try
        {
            samples = await _sampler.SampleAllAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DockerApiException or HttpRequestException or IOException or TimeoutException)
        {
            LogSweepFailed(ex.Message);
            return;
        }

        if (samples.Count > 0)
        {
            await _connection.SendMetricsReportAsync(samples, cancellationToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent is un-enrolled; not sampling server metrics.")]
    private partial void LogNotSamplingUnenrolled();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Metrics sweep skipped this cycle: {Detail}")]
    private partial void LogSweepFailed(string detail);
}
