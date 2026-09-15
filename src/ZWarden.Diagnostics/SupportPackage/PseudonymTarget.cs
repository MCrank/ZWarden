namespace ZWarden.Diagnostics.SupportPackage;

/// <summary>
/// The kind of operational identifier a <see cref="Pseudonymizer"/> replaces (F30, PRD 53). Each kind gets its own
/// numbered token space (<c>&lt;HOST-1&gt;</c>, <c>&lt;PLAYER-2&gt;</c>, <c>&lt;PRIVATE-IP-1&gt;</c>,
/// <c>&lt;PUBLIC-IP-1&gt;</c>). IP kinds are detected from the text; host and player values are supplied by the
/// collector (they cannot be recognised reliably in free text).
/// </summary>
public enum PseudonymCategory
{
    /// <summary>A hostname or machine name.</summary>
    Host,

    /// <summary>A player name or Steam identity.</summary>
    Player,

    /// <summary>An RFC 1918 / loopback / link-local IPv4 address.</summary>
    PrivateIp,

    /// <summary>A publicly-routable IPv4 address.</summary>
    PublicIp,
}

/// <summary>
/// A known value the collector asks the <see cref="Pseudonymizer"/> to replace consistently (F30, PRD 53). Used for
/// the identifiers that cannot be detected from free text on their own — hostnames and player names — where the
/// collector already knows the concrete strings (from the server inventory, the roster, and configuration).
/// </summary>
/// <param name="Value">The exact string to replace wherever it appears.</param>
/// <param name="Category">The token space it maps into.</param>
public sealed record PseudonymTarget(string Value, PseudonymCategory Category);
