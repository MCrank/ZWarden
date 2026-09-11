namespace ZWarden.Infrastructure.Identity;

/// <summary>
/// The password-hashing parameters for F4, <b>chosen against OWASP guidance rather than inherited</b>
/// from the ASP.NET Core Identity default (ADR 0006, PRD 2.1). Identity's <c>PasswordHasher</c> ships
/// PBKDF2-HMAC-SHA512 at 100,000 iterations — ~2.2× below current guidance and invisible in a codebase
/// that writes no configuration. This type makes the choice explicit, dated, and reviewable at the
/// Feature 40 release gate.
/// </summary>
public static class IdentityHashingParameters
{
    /// <summary>
    /// PBKDF2-HMAC-SHA512 iteration count (the v3 hasher's PRF). Set to OWASP's then-current figure for
    /// this PRF. <b>Chosen 2026-09-11</b> (see <see cref="ChosenOnUtc"/>); revisit at the F40 gate.
    /// </summary>
    public const int Pbkdf2IterationCount = 220_000;

    /// <summary>The date <see cref="Pbkdf2IterationCount"/> was chosen, recorded so the value's age is
    /// legible when it is reviewed (ADR 0006 — the point is that the number is chosen, not inherited).</summary>
    public const string ChosenOnUtc = "2026-09-11";

    /// <summary>The OWASP figure the count was measured against, for the record.</summary>
    public const int OwaspReferenceCountAtChoice = 220_000;
}
