using System.Text.RegularExpressions;

namespace ZWarden.Diagnostics.SupportPackage;

/// <summary>
/// One secret the <see cref="SecretScanner"/> found. Reports only <b>which</b> detector fired and <b>where</b> —
/// never the matched value — so a detection can be audited and surfaced to the operator without re-leaking the
/// secret it is refusing to ship (F30, PRD 51).
/// </summary>
/// <param name="DetectorName">The detector that fired (e.g. <c>pem-private-key</c>, <c>jwt</c>).</param>
/// <param name="Offset">The character offset of the match in the scanned text.</param>
public sealed record SecretDetection(string DetectorName, int Offset);

/// <summary>
/// The last-line, content-based secret gate of the support-package pipeline (F30 D-3, PRD 51). It runs over the
/// <b>fully-sanitized</b> payload — after control-strip, key-based redaction, and pseudonymization have removed the
/// expected sensitive material — and looks for high-signal secret <b>shapes</b> that a key-based pass cannot catch:
/// a secret sitting in free text with no key, or in a place redaction did not expect. A detection makes package
/// generation <b>fail</b> rather than emit (the builder aborts). The detector set is deliberately conservative and
/// biased toward failing safe: a false positive fails a package, which PRD 51 explicitly prefers over knowingly
/// emitting a secret. It reports only the detector name and offset — never the value.
/// </summary>
public static partial class SecretScanner
{
    // Ordered high-signal detectors. Structured shapes first (near-zero false positives); the entropy backstop last.
    private static readonly (string Name, Regex Pattern)[] Detectors =
    [
        ("pem-private-key", PemPrivateKey()),
        ("jwt", Jwt()),
        ("aws-access-key", AwsAccessKey()),
        ("github-token", GitHubToken()),
        ("slack-token", SlackToken()),
        ("bearer-token", BearerToken()),
        ("url-credentials", UrlCredentials()),
        ("inline-password", InlinePassword()),
    ];

    /// <summary>Scans <paramref name="text"/> and returns the first detection, or <c>null</c> when the text is
    /// clean. The scan is ordered and deterministic; it stops at the earliest-named detector that matches.</summary>
    public static SecretDetection? Scan(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        foreach ((string name, Regex pattern) in Detectors)
        {
            Match match = pattern.Match(text);
            if (match.Success)
            {
                // inline-password matches the mask itself only if redaction was skipped; guard against masked values.
                if (name == "inline-password" && IsMasked(match.Groups["val"].Value))
                {
                    continue;
                }

                return new SecretDetection(name, match.Index);
            }
        }

        return ScanForHighEntropy(text);
    }

    private static bool IsMasked(string value) => value.Length > 0 && value.All(c => c == '*');

    // A base64/hex-ish token long enough and random enough to be a key or token, not prose. Excludes 32/64-char hex
    // (GUIDs and SHA-256 digests, which we legitimately emit) and anything with clear word structure.
    private static SecretDetection? ScanForHighEntropy(string text)
    {
        foreach (Match match in HighEntropyCandidate().Matches(text))
        {
            string token = match.Value;
            if (IsBenignHexOrGuid(token))
            {
                continue;
            }

            if (ShannonEntropyPerChar(token) >= 4.3)
            {
                return new SecretDetection("high-entropy", match.Index);
            }
        }

        return null;
    }

    private static bool IsBenignHexOrGuid(string token)
    {
        string hex = token.Replace("-", string.Empty);
        // A GUID (32 hex) or a SHA-256 (64 hex) is not a secret shape — we emit both in manifests and ids.
        return (hex.Length is 32 or 64) && hex.All(Uri.IsHexDigit);
    }

    private static double ShannonEntropyPerChar(string token)
    {
        Dictionary<char, int> counts = new();
        foreach (char c in token)
        {
            counts[c] = counts.TryGetValue(c, out int n) ? n + 1 : 1;
        }

        double entropy = 0;
        foreach (int count in counts.Values)
        {
            double p = (double)count / token.Length;
            entropy -= p * Math.Log2(p);
        }

        return entropy;
    }

    [GeneratedRegex(@"-----BEGIN (?:RSA |EC |DSA |OPENSSH |PGP |ENCRYPTED )?PRIVATE KEY-----")]
    private static partial Regex PemPrivateKey();

    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}")]
    private static partial Regex Jwt();

    [GeneratedRegex(@"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b")]
    private static partial Regex AwsAccessKey();

    [GeneratedRegex(@"\b(?:ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9]{20,}\b|\bgithub_pat_[A-Za-z0-9_]{20,}\b")]
    private static partial Regex GitHubToken();

    [GeneratedRegex(@"\bxox[baprs]-[A-Za-z0-9-]{10,}\b")]
    private static partial Regex SlackToken();

    [GeneratedRegex(@"(?i)\bbearer\s+[A-Za-z0-9._\-]{16,}\b")]
    private static partial Regex BearerToken();

    // scheme://user:password@host — credentials embedded in a URL.
    [GeneratedRegex(@"[A-Za-z][A-Za-z0-9+.\-]*://[^\s:/@]+:(?<val>[^\s:/@]+)@")]
    private static partial Regex UrlCredentials();

    // password / pwd / secret / token = value, as a backstop for an unredacted inline secret.
    [GeneratedRegex(@"(?i)\b(?:password|passwd|pwd|secret|apikey|api_key)\b\s*[:=]\s*(?<val>[^\s;,'""]{6,})")]
    private static partial Regex InlinePassword();

    // A contiguous base64/hex-ish run of 40+ characters — the shape of an encoded key or token.
    [GeneratedRegex(@"[A-Za-z0-9+/=_\-]{40,}")]
    private static partial Regex HighEntropyCandidate();
}
