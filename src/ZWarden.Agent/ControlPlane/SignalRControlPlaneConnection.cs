using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
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
    private readonly AgentOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SignalRControlPlaneConnection> _logger;
    private HubConnection? _connection;

    public SignalRControlPlaneConnection(
        IAgentTrustStore trustStore,
        IOptions<AgentOptions> options,
        TimeProvider timeProvider,
        ILogger<SignalRControlPlaneConnection> logger)
    {
        ArgumentNullException.ThrowIfNull(trustStore);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _trustStore = trustStore;
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

        Envelope<AgentHello> hello = Envelope.Create(new AgentHello(agentId), _timeProvider.GetUtcNow(), agentId);
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
            Envelope.Create(AgentStateSnapshot.Empty, _timeProvider.GetUtcNow(), agentId);
        await connection.InvokeAsync(AgentHubProtocol.StateSnapshot, snapshot, cancellationToken).ConfigureAwait(false);
        return result;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Control-plane protocol negotiation was rejected: {Reason}")]
    private partial void LogIncompatible(string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Re-handshake after reconnect failed; will retry on the next reconnect.")]
    private partial void LogReconnectHandshakeFailed(Exception ex);

    // Reconnect forever with an exponential backoff capped at 30s — the Agent should keep trying to reach the
    // control plane rather than give up (SignalR's default policy stops after ~30s).
    private sealed class CappedBackoffRetryPolicy : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext retryContext)
        {
            double seconds = Math.Min(30, Math.Pow(2, Math.Min(retryContext.PreviousRetryCount, 5)));
            return TimeSpan.FromSeconds(seconds);
        }
    }
}
