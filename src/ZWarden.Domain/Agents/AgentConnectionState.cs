namespace ZWarden.Domain.Agents;

/// <summary>
/// An Agent's <b>observed</b> connection state as ZWarden.Web last recorded it (F10) — distinct from its
/// trust state (enabled + credential). It is the persisted companion to the in-memory connection registry
/// (decision 2): the registry is authoritative for "connected right now", while this survives restarts so the
/// operator view has a last-known state and the connection monitor has a staleness signal.
/// </summary>
public enum AgentConnectionState
{
    /// <summary>Not connected as far as ZWarden.Web last observed. The default for a newly enrolled Agent.</summary>
    Disconnected = 0,

    /// <summary>Observed connected: the Agent completed the handshake and has not been seen to disconnect.</summary>
    Connected = 1,
}
