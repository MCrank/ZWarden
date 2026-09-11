using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZWarden.Domain.Ids;

/// <summary>
/// Serializes any <see cref="ITypedId{TSelf}"/> as its canonical <c>&lt;prefix&gt;-&lt;uuid&gt;</c>
/// string (PRD 6 - a raw GUID never crosses a boundary). Register once on
/// <see cref="JsonSerializerOptions.Converters"/>; it handles every typed id.
/// </summary>
public sealed class TypedIdJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        return typeToConvert.IsValueType
            && Array.Exists(typeToConvert.GetInterfaces(), static i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ITypedId<>));
    }

    /// <inheritdoc />
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        => (JsonConverter)Activator.CreateInstance(
            typeof(TypedIdJsonConverter<>).MakeGenericType(typeToConvert))!;
}

internal sealed class TypedIdJsonConverter<T> : JsonConverter<T>
    where T : struct, ITypedId<T>
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? text = reader.GetString();
        if (text is null)
        {
            throw new JsonException($"Expected a '{T.Prefix}' identifier string but found null.");
        }

        try
        {
            return TypedId.Parse<T>(text);
        }
        catch (FormatException ex)
        {
            throw new JsonException(ex.Message, ex);
        }
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(TypedId.Format<T>(value.Value));
    }
}
