using System.Text;

namespace ZWarden.Diagnostics.SupportPackage;

/// <summary>
/// Strips ANSI/CSI/OSC escape sequences and C0/C1 control characters from an untrusted string before it enters a
/// support package (F30, PRD 51 "Sanitize"; trust-boundaries §8). This mirrors the F27
/// <c>LogLineSanitizer</c> posture at the packaging boundary: a Server, its mods, its configuration, or its
/// certificate can emit control bytes that reposition a terminal cursor or smuggle escape sequences into whatever
/// opens the package. Pure and allocation-light so it is unit-tested exhaustively with no I/O. The diagnostic
/// report already bounds each detail (<c>DiagnosticCheck.MaxDetailLength</c>); a defensive length cap here guards
/// the other collected strings.
/// </summary>
public static class SupportPackageSanitizer
{
    private const char Escape = '\x1B';

    /// <summary>The defensive maximum length any single sanitized string is capped to.</summary>
    public const int MaxLength = 4096;

    /// <summary>Returns <paramref name="raw"/> with escape sequences and control characters removed and the result
    /// capped to <see cref="MaxLength"/>. Tab is preserved; newlines and other control bytes are dropped. A null
    /// input yields the empty string.</summary>
    public static string Sanitize(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        StringBuilder builder = new(Math.Min(raw.Length, MaxLength));
        int i = 0;
        while (i < raw.Length && builder.Length < MaxLength)
        {
            char c = raw[i];
            if (c == Escape)
            {
                i = SkipEscapeSequence(raw, i);
                continue;
            }

            if (IsStripped(c))
            {
                i++;
                continue;
            }

            builder.Append(c);
            i++;
        }

        return builder.ToString();
    }

    // Control characters with no display meaning: the C0 range (except tab), DEL, and the C1 range. Newline and
    // carriage return fall in C0 and are stripped — a report detail is a single logical value, not a document.
    private static bool IsStripped(char c) =>
        (c < '\x20' && c != '\t') || c == '\x7F' || (c is >= '\x80' and <= '\x9F');

    // Given raw[start] == ESC, returns the index just past the whole escape sequence: CSI (ESC [ … final byte),
    // OSC (ESC ] … BEL or ST), or a generic two-character escape. Bounds-safe at end of string.
    private static int SkipEscapeSequence(string raw, int start)
    {
        int i = start + 1;
        if (i >= raw.Length)
        {
            return i;
        }

        char next = raw[i];
        if (next == '[')
        {
            i++;
            while (i < raw.Length && raw[i] is < '\x40' or > '\x7E')
            {
                i++;
            }

            return i < raw.Length ? i + 1 : i;
        }

        if (next == ']')
        {
            i++;
            while (i < raw.Length && raw[i] != '\x07' && !(raw[i] == Escape && i + 1 < raw.Length && raw[i + 1] == '\\'))
            {
                i++;
            }

            if (i < raw.Length && raw[i] == '\x07')
            {
                return i + 1;
            }

            return i + 1 < raw.Length ? i + 2 : raw.Length;
        }

        return i + 1;
    }
}
