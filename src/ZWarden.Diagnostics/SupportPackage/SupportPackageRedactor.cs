using System.Text.RegularExpressions;
using ZWarden.Domain.Security;

namespace ZWarden.Diagnostics.SupportPackage;

/// <summary>
/// Masks values that sit behind a conventionally-sensitive key inside an untrusted string before it enters a
/// support package (F30, PRD 51 "Redact"). It reuses the framework-neutral <see cref="Redaction"/> primitives
/// (F3) — the same sensitive-key vocabulary and mask the rest of ZWarden uses — so an inline
/// <c>RconPassword=hunter2</c> or <c>token: abc123</c> becomes <c>RconPassword=***</c> / <c>token: ***</c>.
/// Redaction removes the <b>expected</b> secrets by key; the <see cref="SecretScanner"/> is the content-based
/// backstop for anything sitting in free text with no key. Already-masked values are left unchanged.
/// </summary>
public static partial class SupportPackageRedactor
{
    /// <summary>Masks the value of every <c>key=value</c> / <c>key: value</c> pair in <paramref name="text"/> whose
    /// key is sensitive (per <see cref="Redaction.IsSensitiveKey"/>). Non-sensitive pairs and free text pass
    /// through unchanged.</summary>
    public static string RedactText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        return Assignment().Replace(text, match =>
        {
            string key = match.Groups["key"].Value;
            string value = match.Groups["val"].Value;
            if (!Redaction.IsSensitiveKey(key) || value == Redaction.Mask)
            {
                return match.Value;
            }

            // Preserve the key and separator exactly; replace only the value.
            return match.Value[..^value.Length] + Redaction.Mask;
        });
    }

    // key <sep> value: an identifier-shaped key, ':' or '=' (optionally spaced), then a run of non-delimiter chars.
    // Deliberately conservative — it targets the common inline forms, not arbitrary prose.
    [GeneratedRegex(@"(?<key>[A-Za-z_][A-Za-z0-9_.\-]*)\s*[:=]\s*(?<val>[^\s;,'""]+)")]
    private static partial Regex Assignment();
}
