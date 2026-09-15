using ZWarden.Domain.Agents;
using ZWarden.Domain.Ids;

namespace ZWarden.Application.Agents;

/// <summary>
/// An operator-facing view of one Host in the F35 inventory — a trusted <c>agt-</c> record projected with its
/// self-reported Host facts (D-1) and observed connection state, for the <c>/hosts</c> page (gated on
/// <c>Agent.View</c>). Never carries a credential or its hash.
/// </summary>
/// <param name="Id">The Agent (Host) identifier.</param>
/// <param name="Label">The operator-set enrollment label, if any.</param>
/// <param name="Hostname">The Host's self-reported machine name (D-1); <c>null</c> until reported.</param>
/// <param name="AgentVersion">The Agent's self-reported build version; <c>null</c> until reported.</param>
/// <param name="OsPlatform">The Host's self-reported OS platform label; <c>null</c> until reported.</param>
/// <param name="IsEnabled">Whether the Agent is allowed to connect.</param>
/// <param name="HasCredential">False once the credential is revoked.</param>
/// <param name="ConnectionState">The last-known persisted connection state (survives a Web restart).</param>
/// <param name="IsConnected">Whether the Agent is connected to <b>this</b> Web process right now — the live
/// registry overlay, authoritative over <paramref name="ConnectionState"/>.</param>
/// <param name="LastSeenAt">When the Agent was last observed (connect, heartbeat, or snapshot); <c>null</c>
/// until it first connects.</param>
/// <param name="LastProtocolVersion">The protocol version negotiated on the last connect; <c>null</c> until
/// it first connects.</param>
/// <param name="EnrolledAt">When the Agent was enrolled.</param>
public sealed record HostSummary(
    AgentId Id,
    string? Label,
    string? Hostname,
    string? AgentVersion,
    string? OsPlatform,
    bool IsEnabled,
    bool HasCredential,
    AgentConnectionState ConnectionState,
    bool IsConnected,
    DateTimeOffset? LastSeenAt,
    int? LastProtocolVersion,
    DateTimeOffset EnrolledAt);
