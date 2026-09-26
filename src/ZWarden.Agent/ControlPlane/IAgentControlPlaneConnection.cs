using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.ControlPlane;

/// <summary>
/// The Agent's outbound connection to the ZWarden.Web control plane (F10) — the SignalR/WSS channel it opens
/// once enrolled (ADR 0007). <see cref="StartAsync"/> connects, authenticates with the stored per-Agent
/// credential, negotiates the protocol version (ADR 0020) and sends the initial state snapshot; the
/// implementation reconnects automatically and re-negotiates on reconnect. Heartbeats are driven externally.
/// </summary>
public interface IAgentControlPlaneConnection : IAsyncDisposable
{
    /// <summary>
    /// Connects, negotiates, and sends the initial snapshot. Returns the negotiation outcome; a connection
    /// whose version is incompatible is stopped (there is no point retrying a version the server refuses), and
    /// the incompatible result is returned rather than thrown.
    /// </summary>
    Task<ProtocolNegotiationResult> StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Sends one heartbeat if the connection is live; a no-op while disconnected/reconnecting.</summary>
    Task SendHeartbeatAsync(AgentHealthStatus health, CancellationToken cancellationToken = default);

    /// <summary>Reports a Server's observed run-state transition (F16); a no-op while disconnected/reconnecting.</summary>
    Task SendServerStateChangedAsync(
        ServerId serverId, ServerRunState runState, CancellationToken cancellationToken = default);

    /// <summary>Reports a Server's observed health transition and breakdown (F16); a no-op while
    /// disconnected/reconnecting.</summary>
    Task SendHealthChangedAsync(
        ServerId serverId,
        ServerHealth health,
        string reason,
        HealthBreakdown breakdown,
        CancellationToken cancellationToken = default);

    /// <summary>Reports the latest runtime-metrics sample for each managed Server (F16); a no-op while
    /// disconnected/reconnecting.</summary>
    Task SendMetricsReportAsync(
        IReadOnlyList<ServerMetricsSample> samples, CancellationToken cancellationToken = default);

    /// <summary>Reports the host's memory budget (#230); a no-op while disconnected/reconnecting.</summary>
    Task SendHostCapacityAsync(HostCapacityReport report, CancellationToken cancellationToken = default);

    /// <summary>Stops the connection.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}
