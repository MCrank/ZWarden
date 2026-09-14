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

        var options = new LuaParseOptions(LuaSyntaxOptions.Lua51);
        SyntaxTree tree = LuaSyntaxTree.ParseText(text, options, path: string.Empty);
        var root = (CompilationUnitSyntax)tree.GetRoot();

        // A syntax error is a first-class not-parsed result, with line and column (ADR 0010; PRD 2.3).
        List<PzConfigDiagnostic> errors = [.. tree.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(ToDiagnostic)];
        if (errors.Count > 0)
        {
            return PzConfigReadResult.Failure(errors);
        }

        if (!TryFindRootTable(kind, root, out TableConstructorExpressionSyntax? rootTable, out PzConfigDiagnostic? rootError))
        {
            return PzConfigReadResult.Failure(rootError!);
        }

        if (!TryBuildTable(rootTable!, maxDepth, out PzTable table, out PzConfigDiagnostic? walkError))
        {
            return PzConfigReadResult.Failure(walkError!);
        }

        return PzConfigReadResult.Success(new PzConfigDocument(kind, table));
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
}
