using ZWarden.Domain.Security;

namespace ZWarden.Application.Agents;

/// <summary>
/// The pre-trust exchange: an unknown Agent presents its one-time enrollment secret and receives a
/// per-Agent credential, which creates the trusted Agent record and consumes the enrollment single-use
/// (PRD 63A; ADR 0007). <b>Not permission-gated</b> — the enrollment secret is the authorization here. It
/// runs under the ambient (default) tenant, so the lookup is a normal tenant-filtered read with no
/// <c>IgnoreQueryFilters</c>; hosted, tenant-bound enrollment is F10A (v1.1). Success and failure are both
/// audited; the specific failure reason is recorded server-side but never returned to the Agent.
/// </summary>
public interface IAgentEnrollmentExchange
{
    /// <summary>Redeems <paramref name="presentedSecret"/> for a per-Agent credential, or returns a
    /// generic failure carrying only the audited reason.</summary>
    Task<AgentEnrollmentResult> RedeemAsync(
        SecretString presentedSecret,
        CancellationToken cancellationToken = default);
}
