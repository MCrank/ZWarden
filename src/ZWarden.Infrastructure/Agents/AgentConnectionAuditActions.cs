namespace ZWarden.Infrastructure.Agents;

/// <summary>
/// The stable, machine-readable audit action names for the Agent control-plane connection (F10; F6, ADR
/// 0019). Connection lifecycle is agent-initiated, so these events carry no actor user — only the Agent id
/// and a non-secret reason. No credential is ever recorded under any of these.
/// </summary>
public static class AgentConnectionAuditActions
{
    /// <summary>An Agent's connection was authenticated and accepted at the hub.</summary>
    public const string Connected = "Agent.Connected";

    /// <summary>An Agent's connection ended (cleanly, on keepalive loss, or by a stale-connection sweep).</summary>
    public const string Disconnected = "Agent.Disconnected";

    /// <summary>An Agent's connection attempt was refused (bad credential or an incompatible protocol version);
    /// the specific reason is recorded here, never disclosed to the Agent.</summary>
    public const string ConnectionRejected = "Agent.ConnectionRejected";

    /// <summary>A live Agent connection was dropped because its credential was revoked or the Agent disabled.</summary>
    public const string CredentialRevokedWhileConnected = "Agent.CredentialRevokedWhileConnected";
}
