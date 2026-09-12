namespace ZWarden.Domain.Enrollments;

/// <summary>
/// The lifecycle state of an <see cref="Enrollment"/>. An enrollment is <b>single-use</b> (PRD 63A): it
/// leaves <see cref="Pending"/> exactly once, to either <see cref="Consumed"/> (redeemed for an Agent
/// credential) or <see cref="Revoked"/> (cancelled unused), and never returns.
/// </summary>
public enum EnrollmentStatus
{
    /// <summary>Minted and not yet redeemed or revoked — redeemable until it expires.</summary>
    Pending = 0,

    /// <summary>Redeemed for a per-Agent credential; terminal.</summary>
    Consumed = 1,

    /// <summary>Cancelled before use by an operator; terminal.</summary>
    Revoked = 2,
}
