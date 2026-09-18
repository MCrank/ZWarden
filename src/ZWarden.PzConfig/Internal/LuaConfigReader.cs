using System.Globalization;
using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using Loretta.CodeAnalysis.Text;
using ZWarden.PzConfig.Model;

namespace ZWarden.PzConfig.Internal;

/// <summary>
/// The Loretta-backed reader for the three Lua config files. It <em>parses</em> the file — it never
/// evaluates it (ADR 0010): a file containing <c>os.execute(...)</c> becomes inert syntax nodes and
/// nothing runs. The dialect is pinned to <see cref="LuaSyntaxOptions.Lua51"/> (the default <c>All</c>
/// is a trap — it would accept dialects PZ's Kahlua rejects, research §6.6), so "would the game accept
/// this?" becomes a parse-time check. This is the one type in the project that touches Loretta, and no
/// Loretta type escapes it — the model it returns is entirely ZWarden's own.
/// </summary>
internal static class LuaConfigReader
{
    public static PzConfigReadResult Read(PzConfigKind kind, ReadOnlySpan<byte> bytes, int maxDepth = 16)
    {
        string text = PzText.DecodeUtf8(bytes);

        if (!TryParse(kind, text, maxDepth, out LuaParse parse, out IReadOnlyList<PzConfigDiagnostic> failures))
        {
            return PzConfigReadResult.Failure(failures);
        }

        // The document retains the parse (an internal Loretta tree) so it can edit a value in place and
        // re-emit the file byte-for-byte (F20b). No Loretta type crosses the public surface.
        var backing = new LuaEditBacking(kind, text, parse, maxDepth);

        // Harvest each keyed setting's leading comment for the tooltip side-map (F20c). Best-effort and
        // off the value model: comments are locale-generated output, never content (ADR 0011).
        Dictionary<string, string> comments = HarvestComments(parse.RootTable, maxDepth);
        return PzConfigReadResult.Success(new PzConfigDocument(kind, backing), diagnostics: null, comments);
    }

    // Walks the tree once (explicit stack, never recursing over attacker depth — trust-boundaries §8)
    // collecting each keyed setting's leading '--' comment against its dotted path. Positional entries
    // (spawn sequences) have no dotted path and are skipped; only keyed tables are descended, which is
    // exactly the set a schema/editor can address. The model build already validated the shape, so this
    // is deliberately forgiving: an unrecognised field simply carries no comment.
    internal static Dictionary<string, string> HarvestComments(TableConstructorExpressionSyntax rootTable, int maxDepth)
    {
        var comments = new Dictionary<string, string>(StringComparer.Ordinal);
        var stack = new Stack<HarvestFrame>();
        stack.Push(new HarvestFrame(rootTable.Fields, prefix: string.Empty));

        while (stack.Count > 0)
        {
            HarvestFrame frame = stack.Peek();
            if (frame.Cursor >= frame.Fields.Count || stack.Count > maxDepth + 1)
            {
                stack.Pop();
                continue;
            }

            TableFieldSyntax field = frame.Fields[frame.Cursor];
            frame.Cursor++;

            string? key = KeyName(field);
            if (key is null)
            {
                continue;
            }

            string path = frame.Prefix.Length == 0 ? key : $"{frame.Prefix}.{key}";

            string? comment = ExtractComment(field.GetLeadingTrivia());
            if (comment is not null)
            {
                comments[path] = comment;
            }

            if (ValueOf(field) is TableConstructorExpressionSyntax child)
            {
                stack.Push(new HarvestFrame(child.Fields, path));
            }
        }

        return comments;
    }

    private static string? KeyName(TableFieldSyntax field) => field switch
    {
        IdentifierKeyedTableFieldSyntax identifier => identifier.Identifier.Text,
        ExpressionKeyedTableFieldSyntax expressionKeyed
            when expressionKeyed.Key is LiteralExpressionSyntax literal
                && literal.Token.RawKind == (int)SyntaxKind.StringLiteralToken => literal.Token.ValueText,
        _ => null,
    };

    private static ExpressionSyntax? ValueOf(TableFieldSyntax field) => field switch
    {
        IdentifierKeyedTableFieldSyntax identifier => identifier.Value,
        UnkeyedTableFieldSyntax unkeyed => unkeyed.Value,
        ExpressionKeyedTableFieldSyntax expressionKeyed => expressionKeyed.Value,
        _ => null,
    };

    // Collects the comment trivia leading a field into clean lines (markers removed, each line trimmed,
    // empty lines dropped), joined by '\n'. Returns null when the field has no comment. PZ UI rich-text
    // markup (<BHC>, <RGB:…>, [!]) is left intact here — stripping it is the sanitizer's job (slice 2).
    private static string? ExtractComment(SyntaxTriviaList leading)
    {
        List<string>? lines = null;

        foreach (SyntaxTrivia trivia in leading)
        {
            switch (trivia.RawKind)
            {
                case (int)SyntaxKind.SingleLineCommentTrivia:
                    AddLine(ref lines, StripLineComment(trivia.ToString()));
                    break;

                case (int)SyntaxKind.MultiLineCommentTrivia:
                    foreach (string inner in StripBlockComment(trivia.ToString()).Split('\n'))
                    {
                        AddLine(ref lines, inner.Trim());
                    }

                    break;
            }
        }

        return lines is { Count: > 0 } ? string.Join('\n', lines) : null;

        static void AddLine(ref List<string>? acc, string line)
        {
            if (line.Length > 0)
            {
                (acc ??= []).Add(line);
            }
        }
    }

    private static string StripLineComment(string raw)
    {
        string s = raw.TrimStart();
        if (s.StartsWith("--", StringComparison.Ordinal))
        {
            s = s[2..];
        }

        return s.Trim();
    }

    // Strips the opening --[=*[ and closing ]=*] of a Lua long-bracket block comment, leaving its body.
    private static string StripBlockComment(string raw)
    {
        string s = raw.Trim();
        int start = s.StartsWith("--", StringComparison.Ordinal) ? 2 : 0;
        if (start < s.Length && s[start] == '[')
        {
            start++;
            while (start < s.Length && s[start] == '=')
            {
                start++;
            }

            if (start < s.Length && s[start] == '[')
            {
                start++;
            }
        }

        int end = s.Length;
        if (end > start && s[end - 1] == ']')
        {
            end--;
            while (end > start && s[end - 1] == '=')
            {
                end--;
            }

            if (end > start && s[end - 1] == ']')
            {
                end--;
            }
        }

        return start <= end ? s[start..end] : string.Empty;
    }

    // Parses text into a tree + the kind's root table + the value model, or reports the fatal
    // diagnostics. Shared by Read and by LuaEditBacking's re-parse after every edit, so both agree on
    // the shape and the guards.
    internal static bool TryParse(
        PzConfigKind kind,
        string text,
        int maxDepth,
        out LuaParse parse,
        out IReadOnlyList<PzConfigDiagnostic> failures)
    {
        parse = default;

        var options = new LuaParseOptions(LuaSyntaxOptions.Lua51);
        SyntaxTree tree = LuaSyntaxTree.ParseText(text, options, path: string.Empty);
        var root = (CompilationUnitSyntax)tree.GetRoot();

        // A syntax error is a first-class not-parsed result, with line and column (ADR 0010; PRD 2.3).
        List<PzConfigDiagnostic> errors = [.. tree.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(ToDiagnostic)];
        if (errors.Count > 0)
        {
            failures = errors;
            return false;
        }

        if (!TryFindRootTable(kind, root, out TableConstructorExpressionSyntax? rootTable, out PzConfigDiagnostic? rootError))
        {
            failures = [rootError!];
            return false;
        }

        if (!TryBuildTable(rootTable!, maxDepth, out PzTable table, out PzConfigDiagnostic? walkError))
        {
            failures = [walkError!];
            return false;
        }

        parse = new LuaParse(tree, rootTable!, table);
        failures = [];
        return true;
    }

    // Locates the table the kind expects: the RHS of SandboxVars = { }, or the table the spawn
    // function returns. A shape that is not the expected one is an UnexpectedRoot diagnostic.
    private static bool TryFindRootTable(
        PzConfigKind kind,
        CompilationUnitSyntax root,
        out TableConstructorExpressionSyntax? table,
        out PzConfigDiagnostic? error)
    {
        table = null;
        error = null;

        switch (kind)
        {
            case PzConfigKind.SandboxVars:
                foreach (StatementSyntax statement in root.Statements.Statements)
                {
                    if (statement is AssignmentStatementSyntax assignment
                        && assignment.Variables.Count == 1
                        && string.Equals(assignment.Variables[0].ToString().Trim(), "SandboxVars", StringComparison.Ordinal)
                        && assignment.EqualsValues.Values.Count == 1
                        && assignment.EqualsValues.Values[0] is TableConstructorExpressionSyntax tc)
                    {
                        table = tc;
                        return true;
                    }
                }

                error = UnexpectedRoot("expected a top-level 'SandboxVars = { … }' assignment");
                return false;

            case PzConfigKind.SpawnRegions:
            case PzConfigKind.SpawnPoints:
                string expectedName = kind == PzConfigKind.SpawnRegions ? "SpawnRegions" : "SpawnPoints";
                foreach (StatementSyntax statement in root.Statements.Statements)
                {
                    if (statement is FunctionDeclarationStatementSyntax function
                        && string.Equals(function.Name.ToString().Trim(), expectedName, StringComparison.Ordinal)
                        && TryReturnedTable(function, out TableConstructorExpressionSyntax? returned))
                    {
                        table = returned;
                        return true;
                    }
                }

                error = UnexpectedRoot($"expected a top-level 'function {expectedName}() return {{ … }} end'");
                return false;

            default:
                error = UnexpectedRoot("unsupported Lua config kind");
                return false;
        }
    }

    private static bool TryReturnedTable(FunctionDeclarationStatementSyntax function, out TableConstructorExpressionSyntax? table)
    {
        foreach (StatementSyntax statement in function.Body.Statements)
        {
            if (statement is ReturnStatementSyntax ret
                && ret.Expressions.Count == 1
                && ret.Expressions[0] is TableConstructorExpressionSyntax tc)
            {
                table = tc;
                return true;
            }
        }

        table = null;
        return false;
    }

    // Builds the value tree with an EXPLICIT STACK - never recurses over attacker-controlled depth
    // (trust-boundaries §8). The pre-check has already bounded the depth; maxDepth is a second guard so
    // this method is safe on its own, independent of whether the caller ran the pre-check.
    private static bool TryBuildTable(
        TableConstructorExpressionSyntax rootSyntax,
        int maxDepth,
        out PzTable result,
        out PzConfigDiagnostic? error)
    {
        result = PzTable.Empty;
        error = null;

        var stack = new Stack<Frame>();
        stack.Push(new Frame(rootSyntax, pendingKey: null));
        PzTable? built = null;

        while (stack.Count > 0)
        {
            if (stack.Count > maxDepth + 1)
            {
                error = new PzConfigDiagnostic(
                    PzDiagnosticSeverity.Error,
                    PzConfigDiagnostic.Codes.TooDeep,
                    $"Table nesting exceeded the {maxDepth}-level limit while reading.",
                    PositionOf(rootSyntax));
                return false;
            }

            Frame frame = stack.Peek();

            if (frame.Cursor >= frame.Fields.Count)
            {
                stack.Pop();
                var table = new PzTable(frame.Entries);
                if (stack.Count == 0)
                {
                    built = table;
                }
                else
                {
                    stack.Peek().Entries.Add(new PzTableEntry(frame.PendingKey, table));
                }

                continue;
            }

            TableFieldSyntax field = frame.Fields[frame.Cursor];
            frame.Cursor++;

            if (!TryDecompose(field, out PzKey? key, out ExpressionSyntax? valueExpr, out error))
            {
                return false;
            }

            if (valueExpr is TableConstructorExpressionSyntax childTable)
            {
                stack.Push(new Frame(childTable, key));
            }
            else if (TryScalar(valueExpr!, out PzValue scalar, out error))
            {
                frame.Entries.Add(new PzTableEntry(key, scalar));
            }
            else
            {
                return false;
            }
        }

        result = built ?? PzTable.Empty;
        return true;
    }

    // Splits a field into its (optional) key and its value expression. A bare identifier key and a
    // bracketed string key ["park ranger"] both map to a PzKey; a positional field has no key.
    private static bool TryDecompose(
        TableFieldSyntax field,
        out PzKey? key,
        out ExpressionSyntax? value,
        out PzConfigDiagnostic? error)
    {
        key = null;
        value = null;
        error = null;

        switch (field)
        {
            case IdentifierKeyedTableFieldSyntax identifierKeyed:
                key = PzKey.Identifier(identifierKeyed.Identifier.Text);
                value = identifierKeyed.Value;
                return true;

            case UnkeyedTableFieldSyntax unkeyed:
                value = unkeyed.Value;
                return true;

            case ExpressionKeyedTableFieldSyntax expressionKeyed
                when expressionKeyed.Key is LiteralExpressionSyntax keyLiteral
                    && keyLiteral.Token.RawKind == (int)SyntaxKind.StringLiteralToken:
                key = PzKey.Quoted(keyLiteral.Token.ValueText);
                value = expressionKeyed.Value;
                return true;

            default:
                error = new PzConfigDiagnostic(
                    PzDiagnosticSeverity.Error,
                    PzConfigDiagnostic.Codes.ParseError,
                    "Unsupported table key form; only bare identifiers and quoted string keys are modelled.",
                    PositionOf(field));
                return false;
        }
    }

    private static bool TryScalar(ExpressionSyntax expr, out PzValue value, out PzConfigDiagnostic? error)
    {
        value = null!;
        error = null;

        switch (expr)
        {
            case LiteralExpressionSyntax literal:
                return TryLiteral(literal, negate: false, out value, out error);

            // Negative numbers parse as a unary-minus over a numeric literal.
            case UnaryExpressionSyntax unary
                when unary.OperatorToken.RawKind == (int)SyntaxKind.MinusToken
                    && unary.Operand is LiteralExpressionSyntax operand
                    && operand.Token.RawKind == (int)SyntaxKind.NumericLiteralToken:
                return TryLiteral(operand, negate: true, out value, out error);

            default:
                error = new PzConfigDiagnostic(
                    PzDiagnosticSeverity.Error,
                    PzConfigDiagnostic.Codes.ParseError,
                    "Unsupported value; only booleans, numbers, strings and tables are modelled.",
                    PositionOf(expr));
                return false;
        }
    }

    private static bool TryLiteral(LiteralExpressionSyntax literal, bool negate, out PzValue value, out PzConfigDiagnostic? error)
    {
        value = null!;
        error = null;
        SyntaxToken token = literal.Token;

        switch (token.RawKind)
        {
            case (int)SyntaxKind.TrueKeyword:
                value = new PzBoolean(true);
                return true;

            case (int)SyntaxKind.FalseKeyword:
                value = new PzBoolean(false);
                return true;

            case (int)SyntaxKind.StringLiteralToken:
                value = new PzString(token.ValueText);
                return true;

            case (int)SyntaxKind.NumericLiteralToken:
                return TryNumber(token, negate, out value, out error);

            default:
                error = new PzConfigDiagnostic(
                    PzDiagnosticSeverity.Error,
                    PzConfigDiagnostic.Codes.ParseError,
                    $"Unsupported literal '{token.Text}'.",
                    PositionOf(literal));
                return false;
        }
    }

    private static bool TryNumber(SyntaxToken token, bool negate, out PzValue value, out PzConfigDiagnostic? error)
    {
        value = null!;
        error = null;

        string text = token.Text;
        bool isInteger = text.IndexOfAny(['.', 'e', 'E', 'p', 'P']) < 0;

        double magnitude = token.Value switch
        {
            double d => d,
            long l => l,
            int i => i,
            _ when TryParseLuaNumber(text, out double parsed) => parsed,
            _ => double.NaN,
        };

        if (double.IsNaN(magnitude))
        {
            error = new PzConfigDiagnostic(
                PzDiagnosticSeverity.Error,
                PzConfigDiagnostic.Codes.ParseError,
                $"Could not read the number '{text}'.",
                PositionOf(token.Parent!));
            return false;
        }

        value = new PzNumber(negate ? -magnitude : magnitude, negate ? "-" + text : text, isInteger);
        return true;
    }

    private static bool TryParseLuaNumber(string text, out double value)
    {
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long hex))
        {
            value = hex;
            return true;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static PzConfigDiagnostic UnexpectedRoot(string detail) =>
        new(PzDiagnosticSeverity.Error, PzConfigDiagnostic.Codes.UnexpectedRoot, $"The file did not have the expected shape: {detail}.");

    private static PzConfigDiagnostic ToDiagnostic(Diagnostic diagnostic)
    {
        LinePosition start = diagnostic.Location.GetLineSpan().StartLinePosition;
        return new PzConfigDiagnostic(
            PzDiagnosticSeverity.Error,
            PzConfigDiagnostic.Codes.ParseError,
            diagnostic.GetMessage(CultureInfo.InvariantCulture),
            new PzSourcePosition(start.Line + 1, start.Character + 1));
    }

    private static PzSourcePosition PositionOf(SyntaxNode node)
    {
        LinePosition start = node.GetLocation().GetLineSpan().StartLinePosition;
        return new PzSourcePosition(start.Line + 1, start.Character + 1);
    }

    // One in-progress table build. Holds its own field cursor and accumulated entries; PendingKey is
    // the key (or null, positional) under which the finished table attaches to its parent.
    private sealed class Frame(TableConstructorExpressionSyntax syntax, PzKey? pendingKey)
    {
        public SeparatedSyntaxList<TableFieldSyntax> Fields { get; } = syntax.Fields;

        public PzKey? PendingKey { get; } = pendingKey;

        public List<PzTableEntry> Entries { get; } = [];

        public int Cursor { get; set; }
    }

    // One in-progress comment-harvest table, carrying the dotted-path prefix its keyed children extend.
    private sealed class HarvestFrame(SeparatedSyntaxList<TableFieldSyntax> fields, string prefix)
    {
        public SeparatedSyntaxList<TableFieldSyntax> Fields { get; } = fields;

        public string Prefix { get; } = prefix;

        public int Cursor { get; set; }
    }
}

/// <summary>
/// The retained result of one Lua parse: the Loretta tree, the kind's root table node, and ZWarden's
/// value model built from it. Internal — the Loretta nodes never cross the seam. Held by
/// <see cref="LuaEditBacking"/> to navigate to a value's source span for a surgical edit.
/// </summary>
internal readonly record struct LuaParse(
    SyntaxTree Tree,
    TableConstructorExpressionSyntax RootTable,
    PzTable Model);
