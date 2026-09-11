namespace ZWarden.Domain.Security;

/// <summary>
/// Framework-neutral redaction primitives. Reused by the diagnostics engine and the sanitized support
/// package (F29/F30), which by trust-boundaries.md §8 handle untrusted, attacker-influenced input -
/// redaction masks sensitive material, it never blesses input as clean.
/// </summary>
public static class Redaction
{
    /// <summary>The marker a redacted value is replaced with.</summary>
    public const string Mask = "***";

    private static readonly string[] SensitiveKeyTokens =
    [
        "password", "passwd", "pwd", "secret", "token", "apikey", "api_key",
        "rcon", "connectionstring", "connection_string", "authorization",
        "cookie", "privatekey", "private_key", "mfa", "credential",
    ];

    /// <summary>Replaces the whole value with <see cref="Mask"/>.</summary>
    public static string MaskAll(string? value) => Mask;

    /// <summary>
    /// Reveals only the last <paramref name="visible"/> characters, masking the rest - e.g. a "last 4".
    /// A value no longer than <paramref name="visible"/> is masked entirely, so a short secret is never
    /// exposed in full.
    /// </summary>
    public static string KeepLast(string? value, int visible)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(visible);
        if (string.IsNullOrEmpty(value) || value.Length <= visible)
        {
            return Mask;
        }

        return Mask + value[^visible..];
    }

    /// <summary>
    /// True when <paramref name="key"/> names a field that conventionally carries a secret (matched
    /// case-insensitively as a substring, so <c>RconPassword</c> and <c>DbConnectionString</c> match).
    /// </summary>
    public static bool IsSensitiveKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        foreach (string token in SensitiveKeyTokens)
        {
            if (key.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns <see cref="Mask"/> for a sensitive key, otherwise the value unchanged.</summary>
    public static string? RedactValueFor(string key, string? value) =>
        IsSensitiveKey(key) ? Mask : value;

    /// <summary>Masks the values of every sensitive key in <paramref name="fields"/>, passing benign ones through.</summary>
    public static IReadOnlyDictionary<string, string?> RedactKnownKeys(IReadOnlyDictionary<string, string?> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        Dictionary<string, string?> result = new(fields.Count, StringComparer.Ordinal);
        foreach (KeyValuePair<string, string?> field in fields)
        {
            result[field.Key] = RedactValueFor(field.Key, field.Value);
        }

        return result;
    }
}
