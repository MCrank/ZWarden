using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZWarden.Domain.Security;

/// <summary>
/// The string specialization of <see cref="Secret{T}"/>, given a name of its own because a secret
/// string is by far the common case and reads better in a signature than <c>Secret&lt;string&gt;</c>
/// (the same reasoning as the typed-id pattern, ADR 0014). Same guarantee: it never stringifies its
/// contents - <see cref="ToString"/>, interpolation, JSON and the debugger all render <see cref="Redacted"/>,
/// and the value is reachable only through <see cref="Reveal"/>.
/// </summary>
[JsonConverter(typeof(SecretStringJsonConverter))]
[DebuggerDisplay("***")]
public readonly struct SecretString
{
    /// <summary>The marker rendered wherever a secret would otherwise stringify.</summary>
    public const string Redacted = "***";

    private readonly string? _value;

    /// <summary>Wraps <paramref name="value"/>. Null is rejected - an empty secret is a bug, not a value.</summary>
    public SecretString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _value = value;
    }

    /// <summary>True when this secret holds a value (false only for the default-constructed struct).</summary>
    public bool HasValue => _value is not null;

    /// <summary>Returns the wrapped string. The single explicit, searchable way to read a secret.</summary>
    public string Reveal() =>
        _value ?? throw new InvalidOperationException(
            "This SecretString is empty (default-constructed) and has no value to reveal.");

    /// <summary>Always the redaction marker - never the contents.</summary>
    public override string ToString() => Redacted;
}

internal sealed class SecretStringJsonConverter : JsonConverter<SecretString>
{
    public override SecretString Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException(
            "A SecretString cannot be deserialized from JSON; construct it explicitly from the source value.");

    public override void Write(Utf8JsonWriter writer, SecretString value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(SecretString.Redacted);
    }
}
