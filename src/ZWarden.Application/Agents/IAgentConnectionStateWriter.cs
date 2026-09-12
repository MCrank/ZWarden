using ZWarden.Domain.Ids;

namespace ZWarden.Application.Agents;

/// <summary>
/// Persists an Agent's observed connection state (F10) — the durable companion to the volatile
/// <see cref="IAgentConnectionRegistry"/>. It stamps the <c>Agent</c> record's <c>LastSeenAt</c>,
/// <c>ConnectionState</c> and <c>LastProtocolVersion</c> so the operator view survives a Web restart and the
/// connection monitor has a staleness signal. Implemented in Infrastructure against the tenant-scoped Agent
/// repository; the connecting Agent has no browser session, so writes run under the default-tenant fallback
/// (ADR 0016), as F9's enrollment exchange does.
/// </summary>
public interface IAgentConnectionStateWriter
{
    /// <summary>Records that <paramref name="agentId"/> connected and negotiated <paramref name="protocolVersion"/>.</summary>
    Task MarkConnectedAsync(AgentId agentId, int protocolVersion, CancellationToken cancellationToken = default);

    /// <summary>Advances <paramref name="agentId"/>'s last-seen time on a heartbeat or snapshot.</summary>
    Task MarkHeartbeatAsync(AgentId agentId, CancellationToken cancellationToken = default);

    /// <summary>Records that <paramref name="agentId"/>'s connection ended.</summary>
    Task MarkDisconnectedAsync(AgentId agentId, CancellationToken cancellationToken = default);
}
