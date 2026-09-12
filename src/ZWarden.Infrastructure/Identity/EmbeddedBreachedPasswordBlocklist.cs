using System.Collections.Frozen;

namespace ZWarden.Infrastructure.Identity;

/// <summary>
/// The default <see cref="IBreachedPasswordBlocklist"/>: a small, embedded set of the most common and
/// widely-breached passwords, matched case-insensitively. It is a <b>starter corpus and a seam</b>, not
/// a complete breach database — an installation that wants fuller coverage registers its own
/// <see cref="IBreachedPasswordBlocklist"/> over a downloaded corpus (kept offline; ADR 0006). Shipping
/// a default means the mandatory check is never silently absent.
/// </summary>
public sealed class EmbeddedBreachedPasswordBlocklist : IBreachedPasswordBlocklist
{
    // A curated starter set: the perennial top-of-the-list passwords, including long-but-weak ones that
    // clear a length rule (repeats, keyboard walks, "correct" phrases) - length alone is not strength.
    private static readonly FrozenSet<string> Blocked =
        new[]
        {
            "password", "123456", "123456789", "12345678", "12345", "1234567", "qwerty",
            "111111", "123123", "abc123", "password1", "1234567890", "iloveyou", "admin",
            "welcome", "monkey", "dragon", "letmein", "football", "baseball",
            // length >= 15 weak values, so a length rule cannot mask the breach check
            "passwordpassword", "123456789012345", "qwertyuiopasdfgh", "aaaaaaaaaaaaaaaa",
            "111111111111111", "correctbatteryhorse",
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public bool IsBreached(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        return Blocked.Contains(password);
    }
}
