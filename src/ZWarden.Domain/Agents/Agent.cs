using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;

namespace ZWarden.Domain.Agents;

/// <summary>
/// A <b>trusted Agent</b> record (<c>agt-</c>) — the trust anchor an enrollment exchange creates (ADR 0007).
/// It is distinct from the ZWarden.Agent component's self-identity file (F8), which proves nothing on its
/// own: trust means <i>this record exists, is enabled, and a presented credential matches it</i>. It holds
/// trust state only — the current per-Agent credential <b>hash</b> (never the raw secret, decision 2) and
/// an enabled flag; Agent-to-Server association, inventory and observed health are later features
/// (F14/F16). It is <see cref="ITenantOwned"/> (stamped and filtered by the ambient tenant, ADR 0016) and
/// <see cref="IVersioned"/>.
/// </summary>
public sealed class Agent : IVersioned, ITenantOwned
{
    /// <summary>EF / factory use.</summary>
    public Agent()
    {
    }

    /// <summary>The Agent identifier (<c>agt-&lt;uuid&gt;</c>).</summary>
    public AgentId Id { get; init; } = AgentId.New();

    /// <inheritdoc />
    public TenantId TenantId { get; init; }

    /// <summary>The one-way hash of the current per-Agent credential; empty once the credential is revoked.
    /// The raw credential is shown once at issue/rotation and never stored.</summary>
    public string CredentialHash { get; private set; } = string.Empty;

    /// <summary>An optional operator-facing label (carried from the enrollment); never a secret.</summary>
    public string? Label { get; init; }

    /// <summary>Whether the Agent is allowed to connect. A disabled Agent is refused even with a matching
    /// credential — disablement is the operator's fail-closed switch independent of the credential.</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>The enrollment this Agent was created from (provenance).</summary>
    public EnrollmentId EnrolledVia { get; init; }

    /// <summary>When the Agent was enrolled (UTC).</summary>
    public DateTimeOffset EnrolledAt { get; init; }

    /// <summary>When the credential last changed — issued, rotated or revoked (UTC).</summary>
    public DateTimeOffset CredentialRotatedAt { get; private set; }

    /// <inheritdoc />
    public Guid Version { get; set; }

    /// <summary>The Agent's observed connection state as ZWarden.Web last recorded it (F10). Observed state,
    /// not trust — a disconnected Agent stays trusted. The in-memory registry is authoritative for "connected
    /// now"; this persists the last-known state across restarts.</summary>
    public AgentConnectionState ConnectionState { get; private set; } = AgentConnectionState.Disconnected;

    /// <summary>When the Agent was last observed — its last connect, heartbeat or snapshot (UTC); <c>null</c>
    /// until it first connects. On heartbeat loss the connection monitor marks this record stale; it is never
    /// promoted back to current without a fresh observation (trust-boundaries.md §3).</summary>
    public DateTimeOffset? LastSeenAt { get; private set; }

    /// <summary>The protocol version negotiated on the Agent's last connect (F10, ADR 0020); <c>null</c> until
    /// it first connects.</summary>
    public int? LastProtocolVersion { get; private set; }

    /// <summary>True when the Agent is trusted: enabled and holding a credential. The actual secret match
    /// is the verifier's (Infrastructure); this is the fail-closed gate around it.</summary>
    public bool IsTrusted => IsEnabled && !string.IsNullOrEmpty(CredentialHash);

    /// <summary>
    /// Creates a trusted, enabled Agent holding <paramref name="credentialHash"/>. The <see cref="TenantId"/>
    /// is left unset so the ownership interceptor stamps the ambient tenant on insert (ADR 0016).
    /// </summary>
    public static Agent Enroll(
        string credentialHash,
        EnrollmentId enrolledVia,
        DateTimeOffset now,
        string? label = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialHash);
        return new Agent
        {
            Id = AgentId.New(),
            CredentialHash = credentialHash,
            EnrolledVia = enrolledVia,
            EnrolledAt = now,
            CredentialRotatedAt = now,
            IsEnabled = true,
            Label = label,
        };
    }

    /// <summary>Rotates the credential to a fresh hash; the old credential stops matching immediately.</summary>
    public void RotateCredential(string newCredentialHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newCredentialHash);
        CredentialHash = newCredentialHash;
        CredentialRotatedAt = now;
    }

    /// <summary>Revokes the credential, clearing the hash so nothing matches. The Agent must re-enroll to
    /// regain trust.</summary>
    public void RevokeCredential(DateTimeOffset now)
    {
        CredentialHash = string.Empty;
        CredentialRotatedAt = now;
    }

    /// <summary>Disables the Agent: it is refused connection even with a valid credential.</summary>
    public void Disable() => IsEnabled = false;

    /// <summary>Re-enables a disabled Agent.</summary>
    public void Enable() => IsEnabled = true;

    /// <summary>Records that the Agent connected and negotiated <paramref name="protocolVersion"/> (F10). Observed
    /// state only — it does not change trust.</summary>
    public void MarkConnected(int protocolVersion, DateTimeOffset now)
    {
        ConnectionState = AgentConnectionState.Connected;
        LastProtocolVersion = protocolVersion;
        LastSeenAt = now;
    }

    /// <summary>Advances the last-seen time on a heartbeat or snapshot (F10), leaving the connection state and
    /// negotiated version as they were. Observed state only.</summary>
    public void MarkHeartbeat(DateTimeOffset now) => LastSeenAt = now;

    /// <summary>Records that the Agent's connection ended (F10), keeping the last negotiated version as the
    /// last-known. Observed state only — a dropped connection does not untrust the Agent.</summary>
    public void MarkDisconnected(DateTimeOffset now)
    {
        ConnectionState = AgentConnectionState.Disconnected;
        LastSeenAt = now;
    }
}
