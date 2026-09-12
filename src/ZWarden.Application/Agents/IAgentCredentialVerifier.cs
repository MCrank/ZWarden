using ZWarden.Domain.Ids;
using ZWarden.Domain.Security;

namespace ZWarden.Application.Agents;

/// <summary>
/// Verifies a presented per-Agent credential — the seam F10's connection handshake calls. Returns the
/// <see cref="AgentId"/> of the enabled Agent whose credential matches, or <c>null</c>. <b>Fail-closed:</b>
/// a wrong credential, a revoked one, a disabled Agent, or an unknown credential all return <c>null</c>.
/// </summary>
public interface IAgentCredentialVerifier
{
    /// <summary>Resolves <paramref name="presentedCredential"/> to a trusted Agent, or <c>null</c>.</summary>
    Task<AgentId?> VerifyAsync(
        SecretString presentedCredential,
        CancellationToken cancellationToken = default);
}
