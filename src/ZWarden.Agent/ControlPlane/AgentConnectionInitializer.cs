using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.Health;
using ZWarden.Agent.Trust;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.ControlPlane;

/// <summary>
/// Opens and maintains the Agent's control-plane connection once it is enrolled (F10). If the trust store
/// holds material, it connects (negotiate + initial snapshot) and then sends a heartbeat on
/// <see cref="AgentOptions.HeartbeatInterval"/>; the connection handles reconnect on its own. If the Agent is
/// un-enrolled, it does not connect and the host still runs (F8) — connecting is impossible without a
/// credential. Runs after <c>AgentEnrollmentInitializer</c>, so a just-enrolled Agent has its trust file.
/// </summary>
public sealed partial class AgentConnectionInitializer : BackgroundService
{
    private readonly IAgentTrustStore _trustStore;
    private readonly IAgentControlPlaneConnection _connection;
    private readonly IAgentHealthState _health;
    private readonly AgentOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AgentConnectionInitializer> _logger;

    public AgentConnectionInitializer(
        IAgentTrustStore trustStore,
        IAgentControlPlaneConnection connection,
        IAgentHealthState health,
        IOptions<AgentOptions> options,
        TimeProvider timeProvider,
        ILogger<AgentConnectionInitializer> logger)
    {
        ArgumentNullException.ThrowIfNull(trustStore);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _trustStore = trustStore;
        _connection = connection;
        _health = health;
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
            LogNotConnectingUnenrolled();
            return;
        }

        try
        {
            await _connection.StartAsync(stoppingToken).ConfigureAwait(false);
            LogConnected(material.AgentId);

            using PeriodicTimer timer = new(_options.HeartbeatInterval, _timeProvider);
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await _connection.SendHeartbeatAsync(_health.Current, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _connection.StopAsync(cancellationToken).ConfigureAwait(false);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent {AgentId} connected to the control plane.")]
    private partial void LogConnected(AgentId agentId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Agent is un-enrolled; not connecting to the control plane. Enroll it to connect.")]
    private partial void LogNotConnectingUnenrolled();
}
