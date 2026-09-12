using ZWarden.Contracts.Protocol;

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

    /// <summary>Stops the connection.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}
