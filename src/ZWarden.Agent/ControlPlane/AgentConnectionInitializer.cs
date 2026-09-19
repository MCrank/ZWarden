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
/// If the Agent is un-enrolled at startup but enrolment is still pending (the background retry after a
/// transient/trust failure, #185), it waits on <see cref="AgentEnrollmentSignal"/> and connects as soon as
/// enrolment settles — no restart needed (#195).
/// </summary>
public sealed partial class AgentConnectionInitializer : BackgroundService
{
    private readonly IAgentTrustStore _trustStore;
    private readonly IAgentControlPlaneConnection _connection;
    private readonly IAgentHealthState _health;
    private readonly AgentEnrollmentSignal _enrollmentSignal;
    private readonly AgentOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AgentConnectionInitializer> _logger;

    public AgentConnectionInitializer(
        IAgentTrustStore trustStore,
        IAgentControlPlaneConnection connection,
        IAgentHealthState health,
        AgentEnrollmentSignal enrollmentSignal,
        IOptions<AgentOptions> options,
        TimeProvider timeProvider,
        ILogger<AgentConnectionInitializer> logger)
    {
        ArgumentNullException.ThrowIfNull(trustStore);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(enrollmentSignal);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _trustStore = trustStore;
        _connection = connection;
        _health = health;
        _enrollmentSignal = enrollmentSignal;
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
            // Not enrolled at startup. Rather than giving up until a restart (#195), wait for enrollment to
            // settle — it may enrol later via the background retry (#185) — then load the trust it wrote and
            // connect. Cancellation-safe: the wait unblocks on shutdown, and if enrollment settled without
            // trust (no secret, or a refused secret) there is nothing to connect with and the host stays up.
            LogWaitingForEnrollment();
            try
            {
                await _enrollmentSignal.WaitUntilSettledAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            material = await _trustStore.TryLoadAsync(stoppingToken).ConfigureAwait(false);
            if (material is null)
            {
                LogNotConnectingUnenrolled();
                return;
            }
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
        Message = "Agent is not yet enrolled; waiting for enrollment to complete before connecting to the "
            + "control plane. No restart is needed once enrollment succeeds.")]
    private partial void LogWaitingForEnrollment();

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Agent is un-enrolled; not connecting to the control plane. Enroll it to connect.")]
    private partial void LogNotConnectingUnenrolled();
}
