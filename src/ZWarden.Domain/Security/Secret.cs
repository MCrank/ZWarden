using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZWarden.Domain.Security;

/// <summary>
/// A wrapper around a sensitive value that is <b>safe by construction</b> (PRD 10, PRD 48): it never
/// stringifies its contents. <see cref="ToString"/>, string interpolation, JSON serialization and the
/// debugger all render <see cref="Redacted"/>; the real value is reachable only through the explicit,
/// greppable <see cref="Reveal"/>. Prefer <see cref="SecretString"/> for the common string case.
/// </summary>
[JsonConverter(typeof(SecretJsonConverterFactory))]
[DebuggerDisplay("***")]
public readonly struct Secret<T>
    where T : notnull
{
    /// <summary>The marker rendered wherever a secret would otherwise stringify.</summary>
    public const string Redacted = "***";

    private readonly T? _value;

    /// <summary>Wraps <paramref name="value"/>. Null is rejected - an empty secret is a bug, not a value.</summary>
    public Secret(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _value = value;
    }

    /// <summary>True when this secret holds a value (false only for the default-constructed struct).</summary>
    public bool HasValue => _value is not null;

    /// <summary>Returns the wrapped value. The single explicit, searchable way to read a secret.</summary>
    public T Reveal() =>
        _value ?? throw new InvalidOperationException(
            "This Secret is empty (default-constructed) and has no value to reveal.");

    /// <summary>Always the redaction marker - never the contents.</summary>
    public override string ToString() => Redacted;
}

/// <summary>Serializes any <see cref="Secret{T}"/> as the redaction marker; refuses to deserialize one.</summary>
internal sealed class SecretJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        return typeToConvert.IsGenericType
            && typeToConvert.GetGenericTypeDefinition() == typeof(Secret<>);
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        Type valueType = typeToConvert.GetGenericArguments()[0];
        return (JsonConverter)Activator.CreateInstance(
            typeof(SecretJsonConverter<>).MakeGenericType(valueType))!;
    }
}

internal sealed class SecretJsonConverter<T> : JsonConverter<Secret<T>>
    where T : notnull
{
    public override Secret<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException(
            "A Secret cannot be deserialized from JSON; construct it explicitly from the source value.");

    public override void Write(Utf8JsonWriter writer, Secret<T> value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(Secret<T>.Redacted);
    }
}
