using ZWarden.Domain.Ids;
using ZWarden.Domain.Security;

namespace ZWarden.Application.Agents;

/// <summary>
/// The outcome of an enrollment exchange. On success it carries the new <see cref="AgentId"/> and the
/// per-Agent <see cref="Credential"/> (shown once). On failure it carries only a <see cref="Failure"/>
/// reason, for the audit record — the endpoint maps <b>every</b> failure to one generic response, so the
/// presenting Agent learns nothing that would make the endpoint an oracle.
/// </summary>
public sealed class AgentEnrollmentResult
{
    private AgentEnrollmentResult(
        bool succeeded,
        AgentId? agentId,
        SecretString credential,
        string? label,
        EnrollmentRedemptionFailure? failure)
    {
        Succeeded = succeeded;
        AgentId = agentId;
        Credential = credential;
        Label = label;
        Failure = failure;
    }

    /// <summary>Whether the exchange succeeded.</summary>
    public bool Succeeded { get; }

    /// <summary>The enrolled Agent, on success; otherwise <c>null</c>.</summary>
    public AgentId? AgentId { get; }

    /// <summary>The per-Agent credential, shown once, on success; the empty secret otherwise.</summary>
    public SecretString Credential { get; }

    /// <summary>The operator label carried from the enrollment, on success.</summary>
    public string? Label { get; }

    /// <summary>The failure reason (for audit only), on failure; otherwise <c>null</c>.</summary>
    public EnrollmentRedemptionFailure? Failure { get; }

    /// <summary>A successful exchange.</summary>
    public static AgentEnrollmentResult Success(AgentId agentId, SecretString credential, string? label) =>
        new(true, agentId, credential, label, null);

    /// <summary>A failed exchange carrying only the audited reason.</summary>
    public static AgentEnrollmentResult Failed(EnrollmentRedemptionFailure failure) =>
        new(false, null, default, null, failure);
}
