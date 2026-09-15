using ZWarden.Application.Diagnostics;

namespace ZWarden.Diagnostics.Evaluators;

/// <summary>
/// Evaluates the configured public endpoint's serving-certificate <see cref="TlsProbeFacts"/> into a
/// <see cref="DiagnosticCheck"/> (F29 D-3). A <b>pure</b> function of the facts, a clock, and a warn window — no
/// sockets, no X509 — so the validity/hostname/expiry bands are unit-tested. When no public HTTPS endpoint is
/// configured the check is <see cref="DiagnosticStatus.Skipped"/> (a legitimate HTTP-only self-hosted mode, not a
/// failure); an absent, invalid, mismatched, or expired certificate is <see cref="DiagnosticStatus.Fail"/>; a
/// certificate inside the warn window is <see cref="DiagnosticStatus.Warn"/>. mTLS is out (v1.1, ADR 0007).
/// </summary>
public static class TlsDiagnostic
{
    /// <summary>The default window before expiry within which a still-valid certificate warns.</summary>
    public static readonly TimeSpan DefaultWarnWindow = TimeSpan.FromDays(14);

    /// <summary>Evaluates the TLS facts into a <see cref="DiagnosticDomain.Tls"/> check as of <paramref name="now"/>.</summary>
    public static DiagnosticCheck Evaluate(TlsProbeFacts facts, DateTimeOffset now, TimeSpan? warnWindow = null)
    {
        ArgumentNullException.ThrowIfNull(facts);
        TimeSpan window = warnWindow ?? DefaultWarnWindow;

        if (!facts.HttpsExpected)
        {
            return Check(DiagnosticStatus.Skipped, "No public HTTPS endpoint is configured; TLS not checked.");
        }

        if (facts.Error is { Length: > 0 })
        {
            return Check(DiagnosticStatus.Fail, "The TLS endpoint could not be probed.", facts.Error);
        }

        if (!facts.CertificatePresent)
        {
            return Check(DiagnosticStatus.Fail, "The endpoint presented no serving certificate.", facts.Host);
        }

        if (!facts.ChainValid)
        {
            return Check(DiagnosticStatus.Fail, "The serving certificate chain is not valid.", facts.Host);
        }

        if (!facts.HostnameMatches)
        {
            return Check(DiagnosticStatus.Fail, "The serving certificate does not match the configured host.", facts.Host);
        }

        if (facts.NotAfter is { } notAfter)
        {
            if (notAfter <= now)
            {
                return Check(DiagnosticStatus.Fail, "The serving certificate has expired.", Describe(facts.Host, notAfter));
            }

            if (notAfter - now <= window)
            {
                int days = (int)Math.Ceiling((notAfter - now).TotalDays);
                return Check(DiagnosticStatus.Warn, $"The serving certificate expires in {days} day(s).", Describe(facts.Host, notAfter));
            }
        }

        return Check(DiagnosticStatus.Pass, "The serving certificate is valid.", facts.Host);
    }

    private static DiagnosticCheck Check(DiagnosticStatus status, string summary, string? detail = null) =>
        DiagnosticCheck.Create(DiagnosticDomain.Tls, status, summary, detail);

    private static string Describe(string? host, DateTimeOffset notAfter)
    {
        string when = notAfter.ToString("u", System.Globalization.CultureInfo.InvariantCulture);
        return host is { Length: > 0 } ? $"{host} — not after {when}" : $"not after {when}";
    }
}
