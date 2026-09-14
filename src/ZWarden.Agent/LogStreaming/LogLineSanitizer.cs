using System.Text;

namespace ZWarden.Agent.LogStreaming;

/// <summary>
/// Sanitizes one raw container log line before it is put on the wire (F27, PRD 38 / trust-boundaries.md §8 —
/// container log output is <b>untrusted input</b>). It strips ANSI/CSI (and OSC) escape sequences and other C0/C1
/// control characters — which could otherwise reposition a terminal cursor, inject colour, or smuggle control
/// bytes into a log viewer — keeping only printable text and the tab. It then caps the line length so a single
/// pathological line cannot blow the wire batch or the browser buffer. Pure and allocation-light, so it is unit
/// tested exhaustively with no Docker. ZWarden.Web still renders the result as <b>data</b>, never markup; this is
/// defence in depth at the source, not a substitute for output encoding.
/// </summary>
public static class LogLineSanitizer
{
    private const char Escape = '\x1B';

    /// <summary>
    /// Strips escape sequences and control characters from <paramref name="raw"/> and caps it to
    /// <paramref name="maxCharacters"/> printable characters.
    /// </summary>
    /// <param name="raw">The raw line, already newline-stripped by the frame assembler.</param>
    /// <param name="maxCharacters">The maximum sanitized length; a longer line is cut and reported truncated.</param>
    /// <returns>The sanitized text and whether it was truncated by the length cap.</returns>
    public static (string Text, bool Truncated) Sanitize(string raw, int maxCharacters)
    {
        ArgumentNullException.ThrowIfNull(raw);
        int cap = maxCharacters > 0 ? maxCharacters : 1;

        StringBuilder builder = new(Math.Min(raw.Length, cap));
        bool truncated = false;
        int i = 0;
        while (i < raw.Length)
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

            if (builder.Length >= cap)
            {
                truncated = true;
                break;
            }

            builder.Append(c);
            i++;
        }

        return (builder.ToString(), truncated);
    }

    // Control characters that carry no display meaning in a log line: the C0 range (except tab), DEL, and the C1
    // range. Newline and carriage return fall in C0 and are stripped defensively — a frame is one line already.
    private static bool IsStripped(char c) =>
        (c < '\x20' && c != '\t') || c == '\x7F' || (c is >= '\x80' and <= '\x9F');

    // Given raw[start] == ESC, return the index just past the whole escape sequence. Handles CSI (ESC [ … final),
    // OSC (ESC ] … BEL or ST), and the generic two-character escape (ESC + one byte). Bounds-safe at end of string.
    private static int SkipEscapeSequence(string raw, int start)
    {
        int i = start + 1;
        if (i >= raw.Length)
        {
            return i; // a lone trailing ESC
        }

        char next = raw[i];
        if (next == '[')
        {
            // CSI: parameter/intermediate bytes until a final byte in 0x40–0x7E.
            i++;
            while (i < raw.Length && raw[i] is < '\x40' or > '\x7E')
            {
                i++;
            }

            return i < raw.Length ? i + 1 : i;
        }

        if (next == ']')
        {
            // OSC: terminated by BEL (0x07) or ST (ESC \).
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

        // A generic two-character escape (ESC + one byte).
        return i + 1;
    }
}
