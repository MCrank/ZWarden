using System.Reflection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.Health;
using ZWarden.Agent.Identity;
using ZWarden.Contracts.Protocol;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent;

/// <summary>
/// The Agent runtime's long-running worker (F8). It logs a structured startup banner, holds the
/// runtime <see cref="AgentHealthStatus.Healthy"/> and runs a liveness tick, then shuts down
/// gracefully. With no transport yet, the tick is a shell — F10 replaces its body with the outbound
/// control-plane connection.
/// </summary>
public sealed partial class AgentWorker : BackgroundService
{
    private static readonly string AgentVersion =
        typeof(AgentWorker).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(AgentWorker).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    private readonly IAgentIdentity _identity;
    private readonly IAgentHealthState _health;
    private readonly AgentOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AgentWorker> _logger;

    /// <summary>Creates the worker.</summary>
    public AgentWorker(
        IAgentIdentity identity,
        IAgentHealthState health,
        IOptions<AgentOptions> options,
        TimeProvider timeProvider,
        ILogger<AgentWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _identity = identity;
        _health = health;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        LogStarting(
            _identity.AgentId,
            AgentVersion,
            ProtocolVersion.Current,
            _options.ControlPlaneUri,
            _options.HeartbeatInterval,
            _options.HealthReportInterval);
        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        LogStopping(_identity.AgentId);
        _health.Report(AgentHealthStatus.Unhealthy, "Agent runtime stopping.");
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _health.Report(AgentHealthStatus.Healthy, "Agent runtime started.");

        try
        {
            using PeriodicTimer timer = new(_options.HealthReportInterval, _timeProvider);
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                LogTick(_health.Current);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown: the host cancelled the stopping token. Drain cleanly.
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "ZWarden.Agent starting. AgentId={AgentId} Version={Version} ProtocolVersion={ProtocolVersion} ControlPlaneUri={ControlPlaneUri} HeartbeatInterval={HeartbeatInterval} HealthReportInterval={HealthReportInterval}")]
    private partial void LogStarting(
        AgentId agentId,
        string version,
        int protocolVersion,
        string controlPlaneUri,
        TimeSpan heartbeatInterval,
        TimeSpan healthReportInterval);

    [LoggerMessage(Level = LogLevel.Information, Message = "ZWarden.Agent stopping. AgentId={AgentId}")]
    private partial void LogStopping(AgentId agentId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Agent liveness tick. Health={Health}")]
    private partial void LogTick(AgentHealthStatus health);
}
