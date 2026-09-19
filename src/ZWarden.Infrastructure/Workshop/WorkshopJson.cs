using System.Globalization;
using System.Text.Json;

namespace ZWarden.Infrastructure.Workshop;

/// <summary>
/// Shared, pure helpers for reading <b>untrusted</b> Steam Web API JSON (trust-boundaries.md §8): every read is
/// bounded, wrong-typed or missing fields are tolerated (returning null), and preview urls are constrained to
/// http(s). Used by the key-gated search client; the keyless metadata client predates it and keeps its own
/// equivalents.
/// </summary>
internal static class WorkshopJson
{
    /// <summary>A Steam publishedfileid is a numeric string; reject anything else as an untrusted-input guard.</summary>
    public static bool IsNumericId(string? id) =>
        !string.IsNullOrEmpty(id) && id.Length <= 20 && id.All(char.IsAsciiDigit);

    public static string? ReadString(JsonElement element, string name, int maxLength)
    {
        if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? text = value.GetString();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return text.Length <= maxLength ? text : text[..maxLength];
    }

    public static string? ReadHttpUrl(JsonElement element, string name, int maxLength)
    {
        string? url = ReadString(element, name, maxLength);
        return url is not null && Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) &&
            (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp)
            ? url
            : null;
    }

    // Counts arrive as either a JSON number or a numeric string depending on the API version.
    public static long? ReadLong(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number))
        {
            return number >= 0 ? number : null;
        }

        if (value.ValueKind == JsonValueKind.String &&
            long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
        {
            return parsed >= 0 ? parsed : null;
        }

        return null;
    }

    public static DateTimeOffset? ReadUnixSeconds(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out JsonElement value) &&
            value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long seconds) && seconds > 0)
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }

        return null;
    }
}
