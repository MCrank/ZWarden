using System.Globalization;
using System.Text.RegularExpressions;

namespace ZWarden.Diagnostics.SupportPackage;

/// <summary>
/// Replaces operational PII with consistent, numbered pseudonyms within one support package (F30, PRD 53). A single
/// instance is used for a whole package so a given value always maps to the same token (<c>&lt;HOST-1&gt;</c>,
/// <c>&lt;PLAYER-2&gt;</c>, <c>&lt;PRIVATE-IP-1&gt;</c>, <c>&lt;PUBLIC-IP-1&gt;</c>), which preserves the
/// relationships a maintainer needs to follow while removing the identifying value. The map is per-package and
/// in-memory — it is deliberately <b>not</b> stable across packages (F30 D-4): a durable pseudonym would itself be
/// a re-identification key. Host and player values are supplied by the collector; IPv4 addresses are detected and
/// classified (RFC 1918 / loopback / link-local ⇒ private, else public) from the text.
/// </summary>
public sealed partial class Pseudonymizer
{
    private readonly IReadOnlyList<PseudonymTarget> _targets;
    private readonly Dictionary<string, string> _assigned = new(StringComparer.Ordinal);
    private readonly Dictionary<PseudonymCategory, int> _counters = new();

    /// <summary>Creates a pseudonymizer for one package. <paramref name="targets"/> are the known host and player
    /// values to replace; pass an empty list when only IP detection is wanted.</summary>
    public Pseudonymizer(IReadOnlyList<PseudonymTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);

        // Replace longer values before shorter ones so a value that is a substring of another does not corrupt it.
        _targets = targets
            .Where(t => !string.IsNullOrEmpty(t.Value))
            .OrderByDescending(t => t.Value.Length)
            .ToList();
    }

    /// <summary>Replaces every known target and every detected IPv4 address in <paramref name="text"/> with its
    /// stable per-package token. Returns the text unchanged when there is nothing to replace.</summary>
    public string Apply(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        string result = text;
        foreach (PseudonymTarget target in _targets)
        {
            result = result.Replace(target.Value, TokenFor(target.Value, target.Category), StringComparison.Ordinal);
        }

        result = Ipv4().Replace(result, match =>
        {
            if (!TryClassifyIpv4(match.Value, out PseudonymCategory category))
            {
                return match.Value; // Not a valid dotted-quad (e.g. 999.1.1.1) — leave it alone.
            }

            return TokenFor(match.Value, category);
        });

        return result;
    }

    private string TokenFor(string value, PseudonymCategory category)
    {
        if (_assigned.TryGetValue(value, out string? existing))
        {
            return existing;
        }

        int next = _counters.TryGetValue(category, out int n) ? n + 1 : 1;
        _counters[category] = next;
        string token = $"<{Label(category)}-{next}>";
        _assigned[value] = token;
        return token;
    }

    private static string Label(PseudonymCategory category) => category switch
    {
        PseudonymCategory.Host => "HOST",
        PseudonymCategory.Player => "PLAYER",
        PseudonymCategory.PrivateIp => "PRIVATE-IP",
        PseudonymCategory.PublicIp => "PUBLIC-IP",
        _ => "VALUE",
    };

    // Classifies a candidate dotted-quad. Rejects out-of-range octets; maps RFC 1918, loopback (127/8) and
    // link-local (169.254/16) to private, everything else to public.
    private static bool TryClassifyIpv4(string candidate, out PseudonymCategory category)
    {
        category = PseudonymCategory.PublicIp;
        string[] parts = candidate.Split('.');
        if (parts.Length != 4)
        {
            return false;
        }

        int[] octets = new int[4];
        for (int i = 0; i < 4; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value > 255)
            {
                return false;
            }

            octets[i] = value;
        }

        bool isPrivate =
            octets[0] == 10 ||
            octets[0] == 127 ||
            (octets[0] == 172 && octets[1] is >= 16 and <= 31) ||
            (octets[0] == 192 && octets[1] == 168) ||
            (octets[0] == 169 && octets[1] == 254);

        category = isPrivate ? PseudonymCategory.PrivateIp : PseudonymCategory.PublicIp;
        return true;
    }

    [GeneratedRegex(@"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b")]
    private static partial Regex Ipv4();
}
