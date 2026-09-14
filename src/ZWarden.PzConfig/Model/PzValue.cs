namespace ZWarden.PzConfig.Model;

/// <summary>
/// A node in the parsed configuration value tree — the structured, in-memory view of one PZ config
/// file behind <see cref="IPzConfigDocument"/>. This is a <em>data</em> model: it is produced by
/// parsing (never by evaluating) the file, it preserves the order and the keys the file actually
/// contained — including keys ZWarden has not been taught (ADR 0010's "never silently drop a key we
/// do not understand") — and it carries no library type across the seam.
/// </summary>
/// <remarks>
/// The hierarchy is closed: every value is exactly one of <see cref="PzBoolean"/>,
/// <see cref="PzNumber"/>, <see cref="PzString"/> or <see cref="PzTable"/>. It deliberately does not
/// model functions, operators or any other Lua construct — the four server files use only these
/// four shapes (research §2), and a construct outside them is a parse-level concern, not a value.
/// </remarks>
public abstract class PzValue
{
    private protected PzValue()
    {
    }
}

/// <summary>A Lua boolean (<c>true</c>/<c>false</c>) or an INI value the schema reads as a boolean.</summary>
public sealed class PzBoolean : PzValue
{
    /// <summary>Initializes a boolean value.</summary>
    public PzBoolean(bool value) => Value = value;

    /// <summary>The boolean value.</summary>
    public bool Value { get; }
}

/// <summary>
/// A numeric value. Both the parsed <see cref="Value"/> and the original <see cref="Lexeme"/> are
/// kept: PZ always emits doubles as <c>1.0</c> and integers as <c>6</c> (research §2.1), so the
/// lexeme distinguishes them for validation and for F20b's fidelity-preserving emit.
/// </summary>
public sealed class PzNumber : PzValue
{
    /// <summary>Initializes a numeric value from its parsed form and its original source text.</summary>
    public PzNumber(double value, string lexeme, bool isInteger)
    {
        ArgumentException.ThrowIfNullOrEmpty(lexeme);
        Value = value;
        Lexeme = lexeme;
        IsInteger = isInteger;
    }

    /// <summary>The numeric value.</summary>
    public double Value { get; }

    /// <summary>The exact source text of the number, sign included (e.g. <c>"1.0"</c>, <c>"6"</c>).</summary>
    public string Lexeme { get; }

    /// <summary><see langword="true"/> when the source wrote an integer (no decimal point or exponent).</summary>
    public bool IsInteger { get; }
}

/// <summary>A string value: a Lua quoted string, or an INI value (INI values are always strings).</summary>
public sealed class PzString : PzValue
{
    /// <summary>Initializes a string value with its already-unescaped content.</summary>
    public PzString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    /// <summary>The unescaped string content.</summary>
    public string Value { get; }
}
