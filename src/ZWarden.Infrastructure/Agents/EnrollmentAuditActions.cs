namespace ZWarden.Infrastructure.Agents;

/// <summary>
/// The stable, machine-readable audit action names for the enrollment/trust flow (F6; ADR 0019). The audit
/// currency is a named constant, never an ad-hoc per-call-site string. No credential is ever recorded under
/// any of these — only ids and reasons.
/// </summary>
public static class EnrollmentAuditActions
{
    /// <summary>An operator minted a one-time enrollment token.</summary>
    public const string TokenIssued = "Enrollment.TokenIssued";

    /// <summary>An operator cancelled an unused enrollment.</summary>
    public const string Revoked = "Enrollment.Revoked";

    /// <summary>An Agent redeemed an enrollment for a credential.</summary>
    public const string Redeemed = "Enrollment.Redeemed";

    /// <summary>An enrollment exchange failed (the reason is recorded, never disclosed to the Agent).</summary>
    public const string RedemptionFailed = "Enrollment.RedemptionFailed";

    /// <summary>A trusted Agent record was created by an exchange.</summary>
    public const string AgentEnrolled = "Agent.Enrolled";

    /// <summary>An operator rotated an Agent's credential.</summary>
    public const string CredentialRotated = "Agent.CredentialRotated";

    /// <summary>An operator revoked an Agent's credential.</summary>
    public const string CredentialRevoked = "Agent.CredentialRevoked";

    /// <summary>An operator disabled an Agent.</summary>
    public const string AgentDisabled = "Agent.Disabled";

    /// <summary>An operator re-enabled an Agent.</summary>
    public const string AgentEnabled = "Agent.Enabled";
}
