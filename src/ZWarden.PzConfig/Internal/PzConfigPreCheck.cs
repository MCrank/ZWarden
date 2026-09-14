namespace ZWarden.PzConfig.Internal;

/// <summary>
/// The size and nesting-depth pre-check applied to raw bytes <em>before</em> any parser is invoked
/// (ADR 0010; trust-boundaries §8). It exists because no Lua parser survives hostile nesting: at deep
/// nesting the process dies with an uncatchable <see cref="StackOverflowException"/> — measured at
/// ~1800 levels for Loretta (research §5) — and .NET 10 has no AppDomain to contain it. This scanner
/// is a cheap, single-pass, allocation-free byte state machine; it is not a full lexer and does not
/// need to be. It only decides one thing: could this file's real table nesting exceed the cap?
/// </summary>
/// <remarks>
/// Structural Lua characters (<c>{ } [ ] = " ' - \</c>, newline) are all single-byte ASCII, and every
/// byte of a multi-byte UTF-8 sequence is ≥ 0x80, so it never collides with them — scanning bytes is
/// therefore safe. The scanner is deliberately <em>conservative</em>: where a construct is malformed
/// (an unterminated string or long bracket), it declines to skip and counts the braces that follow,
/// so a nesting bomb can never hide behind an unterminated string. Over-counting a broken file only
/// makes ZWarden reject it a little sooner; the parser remains the authority on well-formed files.
/// </remarks>
internal static class PzConfigPreCheck
{
    /// <summary>
    /// Returns a fatal diagnostic if <paramref name="bytes"/> is too large or nests too deeply for its
    /// <paramref name="kind"/>, or <see langword="null"/> if it is safe to hand to the parser. INI has
    /// no nesting, so the depth scan is skipped for it.
    /// </summary>
    public static PzConfigDiagnostic? Check(ReadOnlySpan<byte> bytes, PzConfigKind kind, PzConfigLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        if (bytes.Length > limits.MaxByteLength)
        {
            return new PzConfigDiagnostic(
                PzDiagnosticSeverity.Error,
                PzConfigDiagnostic.Codes.TooLarge,
                $"The file is {bytes.Length} bytes, over the {limits.MaxByteLength}-byte limit; it was not parsed.");
        }

        if (kind != PzConfigKind.Ini)
        {
            int depth = MaxBraceDepth(bytes, limits.MaxNestingDepth);
            if (depth > limits.MaxNestingDepth)
            {
                return new PzConfigDiagnostic(
                    PzDiagnosticSeverity.Error,
                    PzConfigDiagnostic.Codes.TooDeep,
                    $"The file nests tables at least {depth} deep, over the {limits.MaxNestingDepth}-level limit; it was not parsed.");
            }
        }

        return null;
    }

    /// <summary>
    /// The maximum brace-nesting depth of <paramref name="bytes"/>, ignoring braces inside comments
    /// and strings. Stops early once <paramref name="cap"/> is exceeded — the exact depth of a bomb is
    /// irrelevant, only that it is over the cap. Iterative; never recurses.
    /// </summary>
    internal static int MaxBraceDepth(ReadOnlySpan<byte> bytes, int cap = int.MaxValue)
    {
        int depth = 0;
        int max = 0;
        int i = 0;
        int n = bytes.Length;

        while (i < n)
        {
            byte c = bytes[i];

            // Comment: "--" then either a long-bracket comment or a line comment.
            if (c == (byte)'-' && i + 1 < n && bytes[i + 1] == (byte)'-')
            {
                int afterDashes = i + 2;
                int level = LongBracketLevel(bytes, afterDashes);
                if (level >= 0)
                {
                    int end = SkipLongBracket(bytes, afterDashes, level);
                    if (end < 0)
                    {
                        // Unterminated long comment: decline to skip; count the rest conservatively.
                        i = afterDashes;
                        continue;
                    }

                    i = end;
                    continue;
                }

                i = afterDashes;
                while (i < n && bytes[i] != (byte)'\n')
                {
                    i++;
                }

                continue;
            }

            // Long-bracket string: [[ ... ]] , [=[ ... ]=], etc. A '[' that is not a long-bracket
            // open (e.g. the ["key"] index form) falls through and is ignored.
            if (c == (byte)'[')
            {
                int level = LongBracketLevel(bytes, i);
                if (level >= 0)
                {
                    int end = SkipLongBracket(bytes, i, level);
                    if (end >= 0)
                    {
                        i = end;
                        continue;
                    }
                }

                i++;
                continue;
            }

            // Quoted string.
            if (c == (byte)'"' || c == (byte)'\'')
            {
                int end = SkipQuoted(bytes, i, c);
                if (end >= 0)
                {
                    i = end;
                    continue;
                }

                // Unterminated: decline to skip; count following braces conservatively.
                i++;
                continue;
            }

            if (c == (byte)'{')
            {
                depth++;
                if (depth > max)
                {
                    max = depth;
                    if (max > cap)
                    {
                        return max;
                    }
                }

                i++;
                continue;
            }

            if (c == (byte)'}')
            {
                if (depth > 0)
                {
                    depth--;
                }

                i++;
                continue;
            }

            i++;
        }

        return max;
    }

    // At index i (the first '['), returns the number of '=' in a long-bracket opener '[' '='* '[',
    // or -1 if there is no such opener here.
    private static int LongBracketLevel(ReadOnlySpan<byte> bytes, int i)
    {
        if (i >= bytes.Length || bytes[i] != (byte)'[')
        {
            return -1;
        }

        int j = i + 1;
        int level = 0;
        while (j < bytes.Length && bytes[j] == (byte)'=')
        {
            level++;
            j++;
        }

        return j < bytes.Length && bytes[j] == (byte)'[' ? level : -1;
    }

    // i points at the opening '[' of a level-N long bracket. Returns the index just past the matching
    // ']' '='*N ']', or -1 if it is unterminated.
    private static int SkipLongBracket(ReadOnlySpan<byte> bytes, int i, int level)
    {
        int j = i + 2 + level;
        while (j < bytes.Length)
        {
            if (bytes[j] == (byte)']')
            {
                int k = j + 1;
                int eq = 0;
                while (k < bytes.Length && bytes[k] == (byte)'=')
                {
                    eq++;
                    k++;
                }

                if (eq == level && k < bytes.Length && bytes[k] == (byte)']')
                {
                    return k + 1;
                }
            }

            j++;
        }

        return -1;
    }

    // i points at the opening quote. Returns the index just past the closing quote, or -1 if the
    // string is unterminated (hits a newline or EOF first - Lua short strings cannot span lines).
    private static int SkipQuoted(ReadOnlySpan<byte> bytes, int i, byte quote)
    {
        int j = i + 1;
        while (j < bytes.Length)
        {
            byte c = bytes[j];
            if (c == (byte)'\\')
            {
                j += 2;
                continue;
            }

            if (c == quote)
            {
                return j + 1;
            }

            if (c == (byte)'\n')
            {
                return -1;
            }

            j++;
        }

        return -1;
    }
}
