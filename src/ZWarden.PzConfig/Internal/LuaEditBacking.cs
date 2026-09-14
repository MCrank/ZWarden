using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using Loretta.CodeAnalysis.Text;
using ZWarden.PzConfig.Model;

namespace ZWarden.PzConfig.Internal;

/// <summary>
/// The write backing for the three Lua files. It keeps the Loretta parse tree (ADR 0010's chosen
/// mechanism) and edits a value by locating the value node for a dotted path, then <em>splicing</em>
/// the rendered replacement over exactly that node's span in the retained source. Because only the
/// value's own characters are replaced — never its surrounding trivia — every comment and every byte
/// ZWarden did not mean to touch is preserved by construction, and there is no whole-file
/// regeneration. After each edit the text is re-parsed so the tree and value model stay in step.
/// </summary>
internal sealed class LuaEditBacking : IPzConfigEditBacking
{
    private readonly PzConfigKind _kind;
    private readonly int _maxDepth;
    private string _text;
    private LuaParse _parse;

    public LuaEditBacking(PzConfigKind kind, string text, LuaParse parse, int maxDepth)
    {
        ArgumentNullException.ThrowIfNull(text);
        _kind = kind;
        _text = text;
        _parse = parse;
        _maxDepth = maxDepth;
    }

    public PzTable Model => _parse.Model;

    public PzConfigEditResult TrySetValue(string path, PzValue newValue)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(newValue);

        PzConfigEditResult located = TryLocate(path, out ExpressionSyntax? target);
        if (!located.Ok)
        {
            return located;
        }

        TextSpan span = target!.Span;
        string rendered = RenderLua(newValue);
        string newText = string.Concat(_text.AsSpan(0, span.Start), rendered, _text.AsSpan(span.End));

        // A surgical scalar replacement of a supported value always re-parses; a failure here means a
        // rendering bug, not an operator state, so it is an exception rather than a first-class result.
        if (!LuaConfigReader.TryParse(_kind, newText, _maxDepth, out LuaParse reparsed, out _))
        {
            throw new InvalidOperationException(
                $"Editing '{path}' produced a file that no longer parses; this is a rendering bug, not a config problem.");
        }

        _text = newText;
        _parse = reparsed;
        return PzConfigEditResult.Success;
    }

    public byte[] Emit() => PzText.EncodeUtf8(_text);

    // Walks the root table by dotted path to the target value node. Descending a non-final segment
    // requires a nested table; a final segment that is itself a table is not a settable scalar.
    private PzConfigEditResult TryLocate(string path, out ExpressionSyntax? target)
    {
        target = null;
        string[] segments = path.Split('.');
        if (Array.Exists(segments, s => s.Length == 0))
        {
            return PzConfigEditResult.PathNotFound(path);
        }

        TableConstructorExpressionSyntax current = _parse.RootTable;
        for (int i = 0; i < segments.Length; i++)
        {
            if (!TryFindField(current, segments[i], out ExpressionSyntax? value))
            {
                return PzConfigEditResult.PathNotFound(path);
            }

            bool last = i == segments.Length - 1;
            if (value is TableConstructorExpressionSyntax childTable)
            {
                if (last)
                {
                    return PzConfigEditResult.PathIsTable(path);
                }

                current = childTable;
            }
            else if (last)
            {
                target = value;
                return PzConfigEditResult.Success;
            }
            else
            {
                // A non-final segment resolved to a scalar; there is nothing to descend into.
                return PzConfigEditResult.PathNotFound(path);
            }
        }

        return PzConfigEditResult.PathNotFound(path);
    }

    private static bool TryFindField(TableConstructorExpressionSyntax table, string name, out ExpressionSyntax? value)
    {
        foreach (TableFieldSyntax field in table.Fields)
        {
            if (TryFieldKey(field, out string? key, out ExpressionSyntax fieldValue)
                && key is not null
                && string.Equals(key, name, StringComparison.Ordinal))
            {
                value = fieldValue;
                return true;
            }
        }

        value = null;
        return false;
    }

    // Mirrors LuaConfigReader.TryDecompose: a bare identifier key and a bracketed string key both yield
    // a name; a positional field yields none.
    private static bool TryFieldKey(TableFieldSyntax field, out string? key, out ExpressionSyntax value)
    {
        switch (field)
        {
            case IdentifierKeyedTableFieldSyntax identifierKeyed:
                key = identifierKeyed.Identifier.Text;
                value = identifierKeyed.Value;
                return true;

            case ExpressionKeyedTableFieldSyntax expressionKeyed
                when expressionKeyed.Key is LiteralExpressionSyntax keyLiteral
                    && keyLiteral.Token.RawKind == (int)SyntaxKind.StringLiteralToken:
                key = keyLiteral.Token.ValueText;
                value = expressionKeyed.Value;
                return true;

            case UnkeyedTableFieldSyntax unkeyed:
                key = null;
                value = unkeyed.Value;
                return true;

            default:
                key = null;
                value = null!;
                return false;
        }
    }

    // Renders a scalar as the Lua source text that goes in place of the old value: booleans as keywords,
    // numbers by their exact lexeme (fidelity, research §2.1), strings as a double-quoted, escaped
    // literal (Loretta unescaped the original on read, so a new string is re-escaped deliberately).
    private static string RenderLua(PzValue value) => value switch
    {
        PzBoolean b => b.Value ? "true" : "false",
        PzNumber n => n.Lexeme,
        PzString s => $"\"{EscapeLuaString(s.Value)}\"",
        _ => throw new ArgumentException($"A {value.GetType().Name} is not a settable scalar value.", nameof(value)),
    };

    private static string EscapeLuaString(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length + 2);
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\': builder.Append("\\\\"); break;
                case '"': builder.Append("\\\""); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default: builder.Append(c); break;
            }
        }

        return builder.ToString();
    }
}
