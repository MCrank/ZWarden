using ZWarden.PzConfig.Model;

namespace ZWarden.PzConfig.Internal;

/// <summary>
/// The write backing for <c>&lt;name&gt;.ini</c>. It keeps the file's exact text and edits it by
/// splicing a new value over the span after <c>=</c> on the matching line — the same line-oriented
/// surgical edit the Agent's RCON writer already uses, generalized. Everything else (key, spacing,
/// trailing comment, the exact line endings) is preserved, and emit is BOM-less UTF-8 (a BOM is fatal
/// to PZ, ADR 0010). The reader (<see cref="IniConfigReader"/>) and this backing share one parse.
/// </summary>
internal sealed class IniEditBacking : IPzConfigEditBacking
{
    private string _text;
    private PzTable _model = PzTable.Empty;

    // key -> the character span of its value (everything after '='), for the FIRST occurrence of the
    // key (PZ never repeats a key; "first" and "the" coincide, matching PzTable.TryGet).
    private readonly Dictionary<string, ValueSpan> _spans = new(StringComparer.Ordinal);

    private readonly List<PzConfigDiagnostic> _diagnostics = [];

    public IniEditBacking(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _text = text;
        Reparse();
    }

    /// <summary>Read-phase diagnostics (a malformed line the reader recovered from).</summary>
    public IReadOnlyList<PzConfigDiagnostic> Diagnostics => _diagnostics;

    public PzTable Model => _model;

    public PzConfigEditResult TrySetValue(string path, PzValue newValue)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(newValue);

        // The INI is flat: a dotted path can never match, and an entry is never a table.
        if (!_spans.TryGetValue(path, out ValueSpan span))
        {
            return PzConfigEditResult.PathNotFound(path);
        }

        string rendered = RenderValue(newValue);
        _text = string.Concat(_text.AsSpan(0, span.Start), rendered, _text.AsSpan(span.Start + span.Length));
        Reparse();
        return PzConfigEditResult.Success;
    }

    public byte[] Emit() => PzText.EncodeUtf8(_text);

    // An INI value is raw text after '=' — strings verbatim, numbers by their exact lexeme, booleans as
    // Lua/INI keywords. A table is not a representable INI value.
    private static string RenderValue(PzValue value) => value switch
    {
        PzString s => s.Value,
        PzNumber n => n.Lexeme,
        PzBoolean b => b.Value ? "true" : "false",
        _ => throw new ArgumentException($"A {value.GetType().Name} is not a representable INI value.", nameof(value)),
    };

    // Rebuilds the value model, the value-span map and the diagnostics from the current text. Called on
    // construction and after every edit (offsets shift when a value's length changes); INI files are
    // small enough that a full reparse is cheaper than tracking deltas.
    private void Reparse()
    {
        _spans.Clear();
        _diagnostics.Clear();
        var entries = new List<PzTableEntry>();

        int lineNumber = 0;
        int pos = 0;
        while (pos <= _text.Length)
        {
            int newline = _text.IndexOf('\n', pos);
            int lineEnd = newline < 0 ? _text.Length : newline;
            int contentEnd = lineEnd > pos && _text[lineEnd - 1] == '\r' ? lineEnd - 1 : lineEnd;
            lineNumber++;

            ReadLine(pos, contentEnd, lineNumber, entries);

            if (newline < 0)
            {
                break;
            }

            pos = newline + 1;
        }

        _model = new PzTable(entries);
    }

    private void ReadLine(int start, int end, int lineNumber, List<PzTableEntry> entries)
    {
        // Skip blank and comment lines, matching IniConfigReader.
        int firstNonSpace = start;
        while (firstNonSpace < end && char.IsWhiteSpace(_text[firstNonSpace]))
        {
            firstNonSpace++;
        }

        if (firstNonSpace >= end || _text[firstNonSpace] == '#')
        {
            return;
        }

        int equals = _text.IndexOf('=', start, end - start);
        if (equals < 0)
        {
            _diagnostics.Add(new PzConfigDiagnostic(
                PzDiagnosticSeverity.Warning,
                PzConfigDiagnostic.Codes.ParseError,
                "Line is not a comment, a blank line, or a KEY=value pair; it was skipped.",
                new PzSourcePosition(lineNumber, 1)));
            return;
        }

        string key = _text[start..equals].Trim();
        if (key.Length == 0)
        {
            _diagnostics.Add(new PzConfigDiagnostic(
                PzDiagnosticSeverity.Warning,
                PzConfigDiagnostic.Codes.ParseError,
                "Line has an empty key before '='; it was skipped.",
                new PzSourcePosition(lineNumber, 1)));
            return;
        }

        int valueStart = equals + 1;
        int valueLength = end - valueStart;
        string value = _text.Substring(valueStart, valueLength);
        entries.Add(new PzTableEntry(PzKey.Identifier(key), new PzString(value)));

        // Record the value span of the first occurrence of the key.
        _spans.TryAdd(key, new ValueSpan(valueStart, valueLength));
    }

    private readonly record struct ValueSpan(int Start, int Length);
}
