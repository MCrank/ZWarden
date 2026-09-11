using System.Globalization;

namespace ZWarden.Domain.Ids;

/// <summary>
/// Shared behaviour for every <see cref="ITypedId{TSelf}"/> (ADR 0014). The concrete structs are
/// thin delegations to these methods, so parse/format/validate lives in exactly one place.
/// </summary>
public static class TypedId
{
    /// <summary>
    /// A fresh, non-empty UUIDv7 identifier. NOTE (ADR 0004): <see cref="Guid.CreateVersion7()"/>
    /// has no documented monotonicity, so two ids from the same millisecond have arbitrary
    /// relative order - never assert strict ordering without spacing generation.
    /// </summary>
    public static TSelf New<TSelf>()
        where TSelf : struct, ITypedId<TSelf>
        => TSelf.FromGuid(Guid.CreateVersion7());

    /// <summary>The canonical lowercase <c>&lt;prefix&gt;-&lt;uuid&gt;</c> form (PRD 6).</summary>
    public static string Format<TSelf>(Guid value)
        where TSelf : struct, ITypedId<TSelf>
        => string.Concat(TSelf.Prefix, value.ToString("D", CultureInfo.InvariantCulture));

    /// <summary>
    /// Wraps a raw <see cref="Guid"/> as <typeparamref name="TSelf"/>. A regular static generic
    /// method (unlike the static-abstract <c>TSelf.FromGuid</c>) so it can be used inside the EF
    /// value-converter expression trees.
    /// </summary>
    public static TSelf FromGuid<TSelf>(Guid value)
        where TSelf : struct, ITypedId<TSelf>
        => TSelf.FromGuid(value);

    /// <summary>
    /// Parses the canonical form. Throws <see cref="FormatException"/> - naming the fault - on the
    /// wrong prefix or a malformed UUID.
    /// </summary>
    public static TSelf Parse<TSelf>(string s)
        where TSelf : struct, ITypedId<TSelf>
    {
        ArgumentNullException.ThrowIfNull(s);
        string prefix = TSelf.Prefix;
        if (!s.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new FormatException($"Expected a '{prefix}' identifier but got '{Elide(s)}'.");
        }

        string body = s[prefix.Length..];
        if (!Guid.TryParse(body, out Guid value))
        {
            throw new FormatException($"'{Elide(s)}' has a '{prefix}' prefix but '{Elide(body)}' is not a UUID.");
        }

        return TSelf.FromGuid(value);
    }

    /// <summary>Non-throwing parse of the canonical form.</summary>
    public static bool TryParse<TSelf>(string? s, out TSelf id)
        where TSelf : struct, ITypedId<TSelf>
    {
        id = default;
        if (string.IsNullOrEmpty(s))
        {
            return false;
        }

        string prefix = TSelf.Prefix;
        if (!s.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        if (!Guid.TryParse(s[prefix.Length..], out Guid value))
        {
            return false;
        }

        id = TSelf.FromGuid(value);
        return true;
    }

    private static string Elide(string s)
        => s.Length <= 64 ? s : string.Concat(s.AsSpan(0, 61), "...");
}
