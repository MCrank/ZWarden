namespace ZWarden.Application.Mods;

/// <summary>
/// A Project Zomboid mod id that is safe to write into <c>Mods=</c> (#290 D3). Every id that enters config — a guess
/// parsed from a Workshop description, an id read from <c>mod.info</c>, or one an operator typed — passes
/// <see cref="TryCreate"/> first, so untrusted text can never split or corrupt the semicolon-joined list
/// (trust-boundaries §8). Printable text is allowed (real ids carry spaces, apostrophes, dots and dashes); config
/// separators (<c>; , =</c>), path/escape characters (<c>\ /</c>), quotes, control characters, and leading or
/// trailing whitespace are not. Compared ordinally: PZ mod ids are case-sensitive.
/// </summary>
public readonly record struct PzModId
{
    /// <summary>The longest mod id accepted.</summary>
    public const int MaxLength = 128;

    private static readonly System.Buffers.SearchValues<char> Forbidden =
        System.Buffers.SearchValues.Create(";,=\\/\"");

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

        id = new PzModId(candidate);
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => Value ?? string.Empty;
}
