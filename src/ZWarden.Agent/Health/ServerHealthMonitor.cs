using System.IO;
using System.Net.Http;
using Docker.DotNet;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.ControlPlane;
using ZWarden.Agent.Trust;
using ZWarden.Contracts.Protocol;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Health;

/// <summary>
/// The Agent's periodic server-health monitor (F16). On <see cref="AgentOptions.HealthReportInterval"/> it
/// observes every owned Server (<see cref="IServerHealthObserver"/>) and reports only the <b>transitions</b> —
/// a changed run-state as <c>ServerStateChanged</c>, a changed health rollup as <c>HealthChanged</c> — so a
/// steady fleet is quiet on the wire. The authoritative full picture is the connection's post-(re)connect
/// snapshot; this fills the gaps between snapshots. It does nothing when the Agent is un-enrolled, and a Docker
/// hiccup on one cycle is logged and skipped, never fatal (the next cycle re-observes).
/// </summary>
public sealed partial class ServerHealthMonitor : BackgroundService
{
    private readonly IAgentTrustStore _trustStore;
    private readonly IServerHealthObserver _observer;
    private readonly IAgentControlPlaneConnection _connection;
    private readonly AgentOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ServerHealthMonitor> _logger;
    private readonly Dictionary<ServerId, (ServerRunState RunState, ServerHealth Health)> _last = [];

    public ServerHealthMonitor(
        IAgentTrustStore trustStore,
        IServerHealthObserver observer,
        IAgentControlPlaneConnection connection,
        IOptions<AgentOptions> options,
        TimeProvider timeProvider,
        ILogger<ServerHealthMonitor> logger)
    {
        ArgumentNullException.ThrowIfNull(trustStore);
        ArgumentNullException.ThrowIfNull(observer);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _trustStore = trustStore;
        _observer = observer;
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
            LogNotMonitoringUnenrolled();
            return;
        }

        try
        {
            using PeriodicTimer timer = new(_options.HealthReportInterval, _timeProvider);
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
        IReadOnlyList<ServerObservation> observed;
        try
        {
            observed = await _observer.ObserveAllAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DockerApiException or HttpRequestException or IOException or TimeoutException)
        {
            // A Docker hiccup this cycle: log and wait for the next tick — never tear the monitor down.
            LogSweepFailed(ex.Message);
            return;
        }

        foreach (ServerObservation observation in observed)
        {
            bool isNew = !_last.TryGetValue(observation.ServerId, out (ServerRunState RunState, ServerHealth Health) prev);

            if (isNew || prev.RunState != observation.RunState)
            {
                await _connection.SendServerStateChangedAsync(observation.ServerId, observation.RunState, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (isNew || prev.Health != observation.Health)
            {
                await _connection.SendHealthChangedAsync(
                    observation.ServerId, observation.Health, observation.Reason, observation.Breakdown, cancellationToken)
                    .ConfigureAwait(false);
                LogHealthTransition(observation.ServerId, observation.Health);
            }

            _last[observation.ServerId] = (observation.RunState, observation.Health);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent is un-enrolled; not monitoring server health.")]
    private partial void LogNotMonitoringUnenrolled();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Health sweep skipped this cycle: {Detail}")]
    private partial void LogSweepFailed(string detail);

    [LoggerMessage(Level = LogLevel.Information, Message = "Server {ServerId} health transitioned to {Health}.")]
    private partial void LogHealthTransition(ServerId serverId, ServerHealth health);
}
