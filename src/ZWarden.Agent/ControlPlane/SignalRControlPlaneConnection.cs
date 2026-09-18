using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.Health;
using ZWarden.Agent.LogStreaming;
using ZWarden.Agent.ServerConfig;
using ZWarden.Agent.Trust;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.ControlPlane;

/// <summary>
/// The SignalR implementation of <see cref="IAgentControlPlaneConnection"/> (F10). It builds a
/// <see cref="HubConnection"/> to <c>ControlPlaneUri</c> + <see cref="AgentHubProtocol.Path"/> over WSS, with
/// the stored per-Agent credential supplied as the access token (re-read on every (re)connect, so revocation
/// takes effect), the canonical <see cref="ProtocolJson"/> serializer on the JSON protocol, and automatic
/// reconnect with a capped backoff. On connect and on every reconnect it sends <see cref="AgentHello"/> to
/// negotiate (ADR 0020) and then an empty host-level <see cref="AgentStateSnapshot"/>.
/// </summary>
public sealed partial class SignalRControlPlaneConnection : IAgentControlPlaneConnection
{
    private readonly IAgentTrustStore _trustStore;
    private readonly AgentCommandProcessor _commands;
    private readonly IServerHealthObserver _health;
    private readonly IServerLogSubscriptionService _logSubscriptions;
    private readonly IServerConfigReader _configReader;
    private readonly AgentOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SignalRControlPlaneConnection> _logger;
    private HubConnection? _connection;

    public SignalRControlPlaneConnection(
        IAgentTrustStore trustStore,
        AgentCommandProcessor commands,
        IServerHealthObserver health,
        IServerLogSubscriptionService logSubscriptions,
        IServerConfigReader configReader,
        IOptions<AgentOptions> options,
        TimeProvider timeProvider,
        ILogger<SignalRControlPlaneConnection> logger)
    {
        ArgumentNullException.ThrowIfNull(trustStore);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(logSubscriptions);
        ArgumentNullException.ThrowIfNull(configReader);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _trustStore = trustStore;
        _commands = commands;
        _health = health;
        _logSubscriptions = logSubscriptions;
        _configReader = configReader;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ProtocolNegotiationResult> StartAsync(CancellationToken cancellationToken = default)
    {
        AgentTrustMaterial material = await _trustStore.TryLoadAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Cannot connect to the control plane before enrollment.");

        HubConnection connection = BuildConnection(material.AgentId);
        _connection = connection;

        await connection.StartAsync(cancellationToken).ConfigureAwait(false);
        return await HelloAndSnapshotAsync(material.AgentId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SendHeartbeatAsync(AgentHealthStatus health, CancellationToken cancellationToken = default)
    {
        if (_connection is not { State: HubConnectionState.Connected } connection)
        {
            return;
        }

        Envelope<AgentHeartbeat> envelope = Envelope.Create(new AgentHeartbeat(health), _timeProvider.GetUtcNow());
        await connection.InvokeAsync(AgentHubProtocol.Heartbeat, envelope, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SendServerStateChangedAsync(
        ServerId serverId, ServerRunState runState, CancellationToken cancellationToken = default)
    {
        if (_connection is not { State: HubConnectionState.Connected } connection)
        {
            return;
        }

        Envelope<ServerStateChanged> envelope = Envelope.Create(
            new ServerStateChanged(serverId, runState), _timeProvider.GetUtcNow(), serverId: serverId);
        await connection.SendAsync(AgentHubProtocol.ServerStateChanged, envelope, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SendHealthChangedAsync(
        ServerId serverId,
        ServerHealth health,
        string reason,
        HealthBreakdown breakdown,
        CancellationToken cancellationToken = default)
    {
        if (_connection is not { State: HubConnectionState.Connected } connection)
        {
            return;
        }

        Envelope<HealthChanged> envelope = Envelope.Create(
            new HealthChanged(serverId, health, reason, breakdown), _timeProvider.GetUtcNow(), serverId: serverId);
        await connection.SendAsync(AgentHubProtocol.HealthChanged, envelope, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SendMetricsReportAsync(
        IReadOnlyList<ServerMetricsSample> samples, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (_connection is not { State: HubConnectionState.Connected } connection)
        {
            return;
        }

        Envelope<ServerMetricsReport> envelope = Envelope.Create(
            new ServerMetricsReport(samples), _timeProvider.GetUtcNow());
        await connection.SendAsync(AgentHubProtocol.MetricsReport, envelope, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is not null)
        {
            await _connection.StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }
    }

    private HubConnection BuildConnection(AgentId agentId)
    {
        HubConnection connection = new HubConnectionBuilder()
            .WithUrl(new Uri(new Uri(_options.ControlPlaneUri, UriKind.Absolute), AgentHubProtocol.Path), options =>
                options.AccessTokenProvider = async () =>
                {
                    AgentTrustMaterial? material = await _trustStore.TryLoadAsync().ConfigureAwait(false);
                    return material?.Credential.Reveal();
                })
            .WithAutomaticReconnect(new CappedBackoffRetryPolicy())
            .AddJsonProtocol(json => json.PayloadSerializerOptions = ProtocolJson.Options)
            .Build();

        // Handle commands the control plane dispatches (F11): deserialize, act, and report the result on the
        // same OperationId. A failed handling must not tear the connection down; the operation's lease reaps
        // it if no report arrives.
        connection.On<string>(AgentHubProtocol.ReceiveCommand, async commandJson =>
        {
            try
            {
                // A live progress emitter over this connection (F17): a long SteamCMD update sends interim
                // OperationProgress through it; other commands ignore it. Built here, never a DI singleton, to
                // avoid a cycle with the connection this processor is wired into.
                HubOperationProgressReporter progress = new(connection, _timeProvider);
                Envelope<OperationCompleted>? reply = await _commands.ProcessAsync(commandJson, CancellationToken.None, progress).ConfigureAwait(false);
                if (reply is not null)
                {
                    await connection.SendAsync(AgentHubProtocol.OperationCompleted, reply).ConfigureAwait(false);
                }
            }
#pragma warning disable CA1031 // A bad command must not crash the connection; the lease reaps an unreported operation.
            catch (Exception ex)
            {
                LogCommandFailed(ex);
            }
#pragma warning restore CA1031
        });

        // Live-log subscription control (F27): Web calls these to begin/stop following a Server's logs while an
        // operator is watching. This is transport plumbing, not a command — no OperationId, no reply, no lock (ADR
        // 0030). The emitter is built over this live connection (like HubOperationProgressReporter), so the
        // subscription service never takes a connection dependency.
        connection.On<string>(AgentHubProtocol.StartServerLogStream, serverIdText =>
        {
            if (ServerId.TryParse(serverIdText, out ServerId serverId))
            {
                _logSubscriptions.Start(serverId, new ServerLogEmitter(connection, _timeProvider));
            }
        });

        connection.On<string>(AgentHubProtocol.StopServerLogStream, async serverIdText =>
        {
            if (ServerId.TryParse(serverIdText, out ServerId serverId))
            {
                await _logSubscriptions.StopAsync(serverId).ConfigureAwait(false);
            }
        });

        // Live configuration read (F20c): Web asks for the current contents of one of a Server's config files, with
        // an opaque correlation id it echoes on the reply. This is transport plumbing, not a command — no OperationId,
        // no lock, no audit, no revision (ADR 0041); the read observes and persists nothing. A failed read must not
        // tear the connection down. The emitter is built over this live connection (like the log emitter).
        connection.On<string, string, string>(
            AgentHubProtocol.RequestServerConfigRead,
            async (serverIdText, fileText, correlationId) =>
            {
                try
                {
                    if (string.IsNullOrEmpty(correlationId)
                        || !ServerId.TryParse(serverIdText, out ServerId serverId)
                        || !Enum.TryParse(fileText, ignoreCase: false, out Domain.Configuration.PzConfigFile file)
                        || !Enum.IsDefined(file))
                    {
                        return;
                    }

                    ConfigReadPayload payload =
                        await _configReader.ReadAsync(serverId, file, CancellationToken.None).ConfigureAwait(false);
                    ServerConfigContentEmitter emitter = new(connection, _timeProvider);
                    await emitter.EmitAsync(correlationId, payload, CancellationToken.None).ConfigureAwait(false);
                }
#pragma warning disable CA1031 // A bad or failed read must not crash the connection; Web times the request out.
                catch (Exception ex)
                {
                    LogConfigReadFailed(ex);
                }
#pragma warning restore CA1031
            });

        // A terminal close (auto-reconnect gave up, or an explicit stop) tears down every follow, so none lingers
        // against a dead connection; a transient drop keeps them — auto-reconnect reuses this same connection and
        // the emitter resumes once it is Connected again.
        connection.Closed += async _ =>
        {
            await _logSubscriptions.StopAllAsync().ConfigureAwait(false);
        };

        // On every reconnect, re-negotiate and resend the snapshot so the server never trusts stale state.
        connection.Reconnected += async _ =>
        {
            try
            {
                await HelloAndSnapshotAsync(agentId, CancellationToken.None).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // A failed re-handshake must not crash the reconnect callback; the next reconnect retries.
            catch (Exception ex)
            {
                LogReconnectHandshakeFailed(ex);
            }
#pragma warning restore CA1031
        };

        return connection;
    }

    private async Task<ProtocolNegotiationResult> HelloAndSnapshotAsync(AgentId agentId, CancellationToken cancellationToken)
    {
        HubConnection connection = _connection
            ?? throw new InvalidOperationException("The connection has not been built.");

        Envelope<AgentHello> hello = Envelope.Create(
            new AgentHello(agentId, HostDescriptorProvider.Current), _timeProvider.GetUtcNow(), agentId);
        ProtocolNegotiationResult result = await connection
            .InvokeAsync<ProtocolNegotiationResult>(AgentHubProtocol.Hello, hello, cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsCompatible)
        {
            LogIncompatible(result.RejectionReason ?? "incompatible");
            await connection.StopAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }

        Envelope<AgentStateSnapshot> snapshot =
            Envelope.Create(await BuildSnapshotAsync(cancellationToken).ConfigureAwait(false), _timeProvider.GetUtcNow(), agentId);
        await connection.InvokeAsync(AgentHubProtocol.StateSnapshot, snapshot, cancellationToken).ConfigureAwait(false);
        return result;
    }

    // The authoritative post-(re)connect snapshot: the observed run-state and health of every owned Server (F16).
    // Docker being unreachable must not break the control-plane handshake, so a failed observation sends an empty
    // snapshot — Web keeps the last observed state and marks it stale (trust-boundaries.md §3).
    private async Task<AgentStateSnapshot> BuildSnapshotAsync(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<ServerObservation> observed = await _health.ObserveAllAsync(cancellationToken).ConfigureAwait(false);
            if (observed.Count == 0)
            {
                return AgentStateSnapshot.Empty;
            }

            return new AgentStateSnapshot(
                observed.Select(o => new ServerState(o.ServerId, o.RunState, o.Health)).ToList());
        }
#pragma warning disable CA1031 // A failed observation must not break the handshake; send an empty snapshot instead.
        catch (Exception ex)
        {
            LogSnapshotObservationFailed(ex);
            return AgentStateSnapshot.Empty;
        }
#pragma warning restore CA1031
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Control-plane protocol negotiation was rejected: {Reason}")]
    private partial void LogIncompatible(string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Re-handshake after reconnect failed; will retry on the next reconnect.")]
    private partial void LogReconnectHandshakeFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Handling a dispatched command failed; the operation's lease will reap it.")]
    private partial void LogCommandFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Handling a live configuration read request failed; the control plane will time it out.")]
    private partial void LogConfigReadFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Observing server health for the connect snapshot failed; sending an empty snapshot.")]
    private partial void LogSnapshotObservationFailed(Exception ex);
}
