using ZWarden.Application.Agents;
using ZWarden.Domain.Agents;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Security;

namespace ZWarden.Infrastructure.Agents;

/// <summary>
/// Verifies a presented per-Agent credential — the seam F10's connection handshake calls (ADR 0007). It
/// resolves the Agent by the hash of the presented credential (a revoked Agent stores an empty hash, which
/// no real credential hash equals) and returns the id only when that Agent <see cref="Agent.IsTrusted"/>
/// (enabled and holding a credential). <b>Fail-closed:</b> a wrong, revoked, disabled or unknown credential
/// all return <c>null</c>.
/// </summary>
public sealed class AgentCredentialVerifier : IAgentCredentialVerifier
{
    private readonly AgentRepository _agents;
    private readonly ICredentialHasher _hasher;

    public AgentCredentialVerifier(AgentRepository agents, ICredentialHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(hasher);
        _agents = agents;
        _hasher = hasher;
    }

    /// <inheritdoc />
    public async Task<AgentId?> VerifyAsync(
        SecretString presentedCredential,
        CancellationToken cancellationToken = default)
    {
        if (!presentedCredential.HasValue)
        {
            return null;
        }

        string hash = _hasher.Hash(presentedCredential);
        Agent? agent = await _agents.FindByCredentialHashAsync(hash, cancellationToken).ConfigureAwait(false);
        return agent is { IsTrusted: true } ? agent.Id : null;
    }
}
