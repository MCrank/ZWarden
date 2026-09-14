namespace ZWarden.PzConfig;

/// <summary>1-based source position. Both line and column count from 1, as an operator's editor shows them.</summary>
public readonly record struct PzSourcePosition(int Line, int Column)
{
    /// <inheritdoc/>
    public override string ToString() => $"line {Line}, column {Column}";
}

/// <summary>How serious a <see cref="PzConfigDiagnostic"/> is.</summary>
public enum PzDiagnosticSeverity
{
    /// <summary>An observation, not a problem — e.g. a key with no schema entry, which is preserved.</summary>
    Info,

    /// <summary>A value that is suspect but not fatal.</summary>
    Warning,

    /// <summary>The file did not parse, or a known key holds a wrong-typed or out-of-range value.</summary>
    Error,
}

/// <summary>
/// One operator-facing finding about a config file — a parse failure, a validation problem, or an
/// informational note. "The file did not parse" is a first-class result carried as an
/// <see cref="PzDiagnosticSeverity.Error"/> diagnostic <em>with a <see cref="Position"/></em>, not an
/// exception (ADR 0010; PRD 2.3 supportability): PZ itself refuses to start on a syntax error, so
/// ZWarden finding it first, with a line and column, is the supportability win.
/// </summary>
public sealed record PzConfigDiagnostic(
    PzDiagnosticSeverity Severity,
    string Code,
    string Message,
    PzSourcePosition? Position = null)
{
    /// <summary>Well-known diagnostic codes, so callers and tests match on a code rather than a message.</summary>
    public static class Codes
    {
        /// <summary>The file exceeded the byte-length cap; the parser was never invoked.</summary>
        public const string TooLarge = "config.too-large";

        /// <summary>The file exceeded the nesting-depth cap; the parser was never invoked.</summary>
        public const string TooDeep = "config.too-deep";

        /// <summary>The file did not parse (Lua syntax error, or a malformed INI line).</summary>
        public const string ParseError = "config.parse-error";

        /// <summary>The root shape was not the one the kind requires (e.g. no <c>SandboxVars = { }</c>).</summary>
        public const string UnexpectedRoot = "config.unexpected-root";

        /// <summary>A known key held a value of the wrong type.</summary>
        public const string WrongType = "config.wrong-type";

        /// <summary>A known numeric key held a value outside its allowed range.</summary>
        public const string OutOfRange = "config.out-of-range";

        /// <summary>A required key or structure was missing.</summary>
        public const string Missing = "config.missing";

        /// <summary>A key ZWarden has no schema entry for; it is preserved and left unvalidated.</summary>
        public const string UnknownKey = "config.unknown-key";
    }
}
