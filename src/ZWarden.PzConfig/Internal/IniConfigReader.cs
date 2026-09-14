using ZWarden.PzConfig.Model;

namespace ZWarden.PzConfig.Internal;

/// <summary>
/// The hand-written reader for <c>&lt;name&gt;.ini</c>: <c># comment</c> and <c>KEY=value</c> lines,
/// no sections and no continuations (research §7 — the game regenerates the comments on every start,
/// so nothing heavier is warranted, and Loretta has no business here). Every value is a string; the
/// schema is what interprets a value as a bool or a number. The reader is line-independent, so a
/// malformed line becomes a recoverable diagnostic (with its line number) and the rest still reads.
/// </summary>
internal static class IniConfigReader
{
    public static PzConfigReadResult Read(ReadOnlySpan<byte> bytes)
    {
        string text = PzText.DecodeUtf8(bytes);
        string[] lines = text.Split('\n');

        var entries = new List<PzTableEntry>();
        var diagnostics = new List<PzConfigDiagnostic>();

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.EndsWith('\r'))
            {
                line = line[..^1];
            }

            // A final "\n" makes Split produce a trailing empty element; skip blank lines generally.
            string leading = line.TrimStart();
            if (leading.Length == 0 || leading[0] == '#')
            {
                continue;
            }

            int equals = line.IndexOf('=', StringComparison.Ordinal);
            if (equals < 0)
            {
                diagnostics.Add(new PzConfigDiagnostic(
                    PzDiagnosticSeverity.Warning,
                    PzConfigDiagnostic.Codes.ParseError,
                    "Line is not a comment, a blank line, or a KEY=value pair; it was skipped.",
                    new PzSourcePosition(i + 1, 1)));
                continue;
            }

            string key = line[..equals].Trim();
            if (key.Length == 0)
            {
                diagnostics.Add(new PzConfigDiagnostic(
                    PzDiagnosticSeverity.Warning,
                    PzConfigDiagnostic.Codes.ParseError,
                    "Line has an empty key before '='; it was skipped.",
                    new PzSourcePosition(i + 1, 1)));
                continue;
            }

            string value = line[(equals + 1)..];
            entries.Add(new PzTableEntry(PzKey.Identifier(key), new PzString(value)));
        }

        var document = new PzConfigDocument(PzConfigKind.Ini, new PzTable(entries));
        return PzConfigReadResult.Success(document, diagnostics);
    }
}
