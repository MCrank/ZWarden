using ZWarden.Domain.Ids;
using ZWarden.Domain.Security;

namespace ZWarden.Application.Agents;

/// <summary>
/// The operator-facing Agent trust-management surface (ADR 0007). Every method authorizes the actor
/// against <c>Tenant.Enrollment.Manage</c> (fail-closed) and audits the outcome (F6), with no credential
/// in any audit record. Rotation returns a fresh credential shown once; revoke, rotate and disable each
/// take effect on the next verification.
/// </summary>
public interface IAgentTrustService
{
    /// <summary>Lists the tenant's Agents (never their credentials or hashes).</summary>
    Task<IReadOnlyList<AgentSummary>> ListAsync(
        UserId actor,
        CancellationToken cancellationToken = default);

    /// <summary>Rotates the Agent's credential and returns the new secret, shown once. The old credential
    /// stops matching immediately.</summary>
    Task<SecretString> RotateCredentialAsync(
        UserId actor,
        AgentId agentId,
        CancellationToken cancellationToken = default);

    /// <summary>Revokes the Agent's credential — it can no longer connect until it re-enrolls.</summary>
    Task RevokeCredentialAsync(
        UserId actor,
        AgentId agentId,
        CancellationToken cancellationToken = default);

    /// <summary>Disables the Agent — refused even with a valid credential.</summary>
    Task DisableAsync(
        UserId actor,
        AgentId agentId,
        CancellationToken cancellationToken = default);

    /// <summary>Re-enables a disabled Agent.</summary>
    Task EnableAsync(
        UserId actor,
        AgentId agentId,
        CancellationToken cancellationToken = default);

    /// <summary>Removes the Host (#363): deletes the Agent record (so its credential no longer verifies) and drops its
    /// live connection. Refused, with nothing changed, while the Host still has Servers. Nothing is done on the
    /// machine itself; bringing it back takes a new enrollment, which creates a new Host.</summary>
    Task<HostRemovalResult> RemoveAsync(
        UserId actor,
        AgentId agentId,
        CancellationToken cancellationToken = default);
}
