using System.Security.Claims;
using ZWarden.Domain.Ids;

namespace ZWarden.Web.Agents;

/// <summary>
/// The claim the <see cref="AgentAuthenticationHandler"/> mints for an authenticated Agent connection (F10):
/// the resolved <see cref="AgentId"/> the credential verifier returned, carried on the connection's principal
/// so the hub lifecycle can address the Agent without re-verifying. It is never a tenant or user claim — an
/// Agent connection carries no session.
/// </summary>
public static class AgentClaims
{
    /// <summary>The claim type carrying the canonical Agent id (<c>agt-&lt;uuid&gt;</c>).</summary>
    public const string AgentIdClaimType = "zwarden:agent";

    /// <summary>Reads the <see cref="AgentId"/> off <paramref name="principal"/>, or <c>false</c> if absent/unparseable.</summary>
    public static bool TryGetAgentId(ClaimsPrincipal? principal, out AgentId agentId)
    {
        string? value = principal?.FindFirst(AgentIdClaimType)?.Value;
        if (value is not null && AgentId.TryParse(value, out agentId))
        {
            return true;
        }

        agentId = default;
        return false;
    }
}
