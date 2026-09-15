using ZWarden.Application.Diagnostics;
using ZWarden.Diagnostics.Evaluators;

namespace ZWarden.Diagnostics.Tests.Evaluators;

/// <summary>
/// F29 PR-A: the pure TLS evaluator (D-3) — a public serving-certificate probe. A total function of the facts, a
/// clock, and a warn window: HTTP-only is Skipped; absent/invalid/mismatched/expired is Fail; inside the window is
/// Warn; otherwise Pass. No sockets, no X509 here.
/// </summary>
public class TlsDiagnosticTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private static TlsProbeFacts Valid(DateTimeOffset notAfter) =>
        new(HttpsExpected: true, Host: "zwarden.example", CertificatePresent: true, ChainValid: true, HostnameMatches: true, NotAfter: notAfter);

    [Test]
    public async Task An_http_only_deployment_is_skipped()
    {
        DiagnosticCheck check = TlsDiagnostic.Evaluate(
            new TlsProbeFacts(HttpsExpected: false, Host: null, CertificatePresent: false, ChainValid: false, HostnameMatches: false, NotAfter: null),
            Now);

        await Assert.That(check.Domain).IsEqualTo(DiagnosticDomain.Tls);
        await Assert.That(check.Status).IsEqualTo(DiagnosticStatus.Skipped);
    }

    [Test]
    public async Task A_probe_error_fails()
    {
        DiagnosticCheck check = TlsDiagnostic.Evaluate(
            new TlsProbeFacts(HttpsExpected: true, Host: "zwarden.example", CertificatePresent: false, ChainValid: false, HostnameMatches: false, NotAfter: null, Error: "connection timed out"),
            Now);

        await Assert.That(check.Status).IsEqualTo(DiagnosticStatus.Fail);
        await Assert.That(check.Detail).IsEqualTo("connection timed out");
    }

    [Test]
    public async Task A_missing_certificate_fails()
    {
        DiagnosticCheck check = TlsDiagnostic.Evaluate(
            new TlsProbeFacts(HttpsExpected: true, Host: "zwarden.example", CertificatePresent: false, ChainValid: false, HostnameMatches: false, NotAfter: null),
            Now);

        await Assert.That(check.Status).IsEqualTo(DiagnosticStatus.Fail);
    }

    [Test]
    public async Task An_invalid_chain_fails()
    {
        DiagnosticCheck check = TlsDiagnostic.Evaluate(Valid(Now.AddDays(90)) with { ChainValid = false }, Now);

        await Assert.That(check.Status).IsEqualTo(DiagnosticStatus.Fail);
        await Assert.That(check.Summary).Contains("chain");
    }

    [Test]
    public async Task A_hostname_mismatch_fails()
    {
        DiagnosticCheck check = TlsDiagnostic.Evaluate(Valid(Now.AddDays(90)) with { HostnameMatches = false }, Now);

        await Assert.That(check.Status).IsEqualTo(DiagnosticStatus.Fail);
        await Assert.That(check.Summary).Contains("host");
    }

    [Test]
    public async Task An_expired_certificate_fails()
    {
        DiagnosticCheck check = TlsDiagnostic.Evaluate(Valid(Now.AddDays(-1)), Now);

        await Assert.That(check.Status).IsEqualTo(DiagnosticStatus.Fail);
        await Assert.That(check.Summary).Contains("expired");
    }

    [Test]
    public async Task A_certificate_inside_the_warn_window_warns()
    {
        DiagnosticCheck check = TlsDiagnostic.Evaluate(Valid(Now.AddDays(7)), Now, TimeSpan.FromDays(14));

        await Assert.That(check.Status).IsEqualTo(DiagnosticStatus.Warn);
        await Assert.That(check.Summary).Contains("day");
    }

    [Test]
    public async Task A_certificate_beyond_the_warn_window_passes()
    {
        DiagnosticCheck check = TlsDiagnostic.Evaluate(Valid(Now.AddDays(90)), Now, TimeSpan.FromDays(14));

        await Assert.That(check.Status).IsEqualTo(DiagnosticStatus.Pass);
    }

    [Test]
    public async Task A_valid_certificate_with_no_expiry_known_passes()
    {
        DiagnosticCheck check = TlsDiagnostic.Evaluate(Valid(Now.AddDays(90)) with { NotAfter = null }, Now);

        await Assert.That(check.Status).IsEqualTo(DiagnosticStatus.Pass);
    }
}
