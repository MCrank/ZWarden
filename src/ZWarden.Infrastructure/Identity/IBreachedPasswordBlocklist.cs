namespace ZWarden.Infrastructure.Identity;

/// <summary>
/// The offline seam NIST SP 800-63-4 requires: every candidate password is checked against a blocklist
/// of known-compromised/common values <b>before it is accepted</b> (ADR 0006). It is deliberately
/// synchronous and offline — the credential path makes no network call — so an installation can swap a
/// fuller corpus (e.g. a Have-I-Been-Pwned export) behind the same interface without changing the
/// validator or reaching out at sign-up time.
/// </summary>
public interface IBreachedPasswordBlocklist
{
    /// <summary>Returns <see langword="true"/> if <paramref name="password"/> appears in the blocklist.
    /// Implementations compare case-insensitively; the check is a membership test, never a network call.</summary>
    bool IsBreached(string password);
}
