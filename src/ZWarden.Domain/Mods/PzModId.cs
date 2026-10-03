namespace ZWarden.Domain.Mods;

/// <summary>
/// A Project Zomboid mod id that is safe to write into <c>Mods=</c> (#290 D3). Every id that enters config — a guess
/// parsed from a Workshop description, an id read from <c>mod.info</c>, or one an operator typed — passes
/// <see cref="TryCreate"/> first, so untrusted text can never split or corrupt the semicolon-joined list
/// (trust-boundaries §8). Printable text is allowed (real ids carry spaces, apostrophes, dots and dashes); config
/// separators (<c>; , =</c>), the escape character <c>\</c>, quotes, control characters, and leading or trailing
/// whitespace are not. One <c>/</c> is allowed only in Build 42's Workshop-qualified form
/// <c>&lt;workshop id&gt;/&lt;id&gt;</c> (e.g. <c>1299328280/ToadTraits</c>): a B42 <c>mod.info</c> may declare it, and
/// PZ 42.21 then loads only that form (spike #291) — so any other slash, such as a path, stays rejected. Compared
/// ordinally: PZ mod ids are case-sensitive.
/// </summary>
public readonly record struct PzModId
{
    /// <summary>The longest mod id accepted.</summary>
    public const int MaxLength = 128;

    // A Steam Workshop id: decimal digits, at most 20 (a ulong).
    private const int MaxWorkshopPrefixLength = 20;

    private static readonly System.Buffers.SearchValues<char> Forbidden =
        System.Buffers.SearchValues.Create(";,=\\\"");

    private PzModId(string value) => Value = value;

    /// <summary>The validated id text.</summary>
    public string Value { get; }

    /// <summary>Validates <paramref name="candidate"/> as a mod id.</summary>
    /// <param name="candidate">The untrusted text.</param>
    /// <param name="id">The validated id when this returns <see langword="true"/>; otherwise the default.</param>
    /// <returns>Whether the candidate is a safe mod id.</returns>
    public static bool TryCreate(string? candidate, out PzModId id)
    {
        id = default;
        if (string.IsNullOrEmpty(candidate)
            || candidate.Length > MaxLength
            || char.IsWhiteSpace(candidate[0])
            || char.IsWhiteSpace(candidate[^1])
            || candidate.AsSpan().ContainsAny(Forbidden))
        {
            return false;
        }

        foreach (char c in candidate)
        {
            if (char.IsControl(c))
            {
                return false;
            }
        }

        int slash = candidate.IndexOf('/', StringComparison.Ordinal);
        if (slash >= 0 && !IsWorkshopQualified(candidate, slash))
        {
            return false;
        }

        id = new PzModId(candidate);
        return true;
    }

    // "<digits>/<id>": one slash, a Workshop id before it, and a non-blank id after it.
    private static bool IsWorkshopQualified(string candidate, int slash) =>
        slash is > 0 and <= MaxWorkshopPrefixLength
        && candidate.AsSpan(0, slash).IndexOfAnyExceptInRange('0', '9') < 0
        && slash < candidate.Length - 1
        && !char.IsWhiteSpace(candidate[slash + 1])
        && candidate.IndexOf('/', slash + 1) < 0;

    /// <inheritdoc />
    public override string ToString() => Value ?? string.Empty;
}
