namespace ZWarden.Application.Agents;

/// <summary>
/// Why an enrollment exchange failed. Audited server-side for the operator's benefit, but <b>never</b>
/// disclosed to the presenting Agent — the endpoint returns one generic failure for all of these, so it
/// is not an oracle for probing which secrets exist or in what state.
/// </summary>
public enum EnrollmentRedemptionFailure
{
    /// <summary>No enrollment matches the presented secret.</summary>
    UnknownSecret = 0,

    /// <summary>The enrollment exists but has passed its expiry.</summary>
    Expired = 1,

    /// <summary>The enrollment has already been consumed (single-use).</summary>
    AlreadyConsumed = 2,

    /// <summary>The enrollment was revoked before use.</summary>
    Revoked = 3,
}
