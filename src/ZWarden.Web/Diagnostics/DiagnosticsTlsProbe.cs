using System.Diagnostics.CodeAnalysis;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using ZWarden.Application.Diagnostics;

namespace ZWarden.Web.Diagnostics;

/// <summary>
/// Gathers the configured public endpoint's serving-certificate <see cref="TlsProbeFacts"/> (F29 D-3): a
/// read-only outbound TLS handshake to <see cref="DiagnosticsOptions.PublicHttpsUrl"/> that inspects the
/// certificate the endpoint presents. It reads the <b>public</b> certificate only — never a private key — and
/// deliberately completes the handshake even for an invalid certificate so the check can report <i>why</i> it is
/// invalid (expired, wrong host, broken chain). When no public HTTPS URL is configured the check is Skipped
/// (HTTP-only self-hosted mode). mTLS and the Agent transport are out (v1.1, ADR 0007). The pure verdict is
/// <c>TlsDiagnostic</c>'s.
/// </summary>
public sealed class DiagnosticsTlsProbe : IDiagnosticsTlsProbe
{
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(5);

    private readonly DiagnosticsOptions _options;

    public DiagnosticsTlsProbe(DiagnosticsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    [SuppressMessage(
        "Security",
        "CA5359:Do not disable certificate validation",
        Justification = "F29 D-3: this is a read-only diagnostic that must inspect an invalid certificate to report why " +
            "it is invalid (expired/wrong-host/broken-chain). The callback captures the SslPolicyErrors instead of " +
            "trusting the endpoint, and the connection transmits nothing and is closed immediately — no data or " +
            "credential is ever sent over it, so accepting the certificate for inspection is safe.")]
    public async Task<TlsProbeFacts> ProbeAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.PublicHttpsUrl)
            || !Uri.TryCreate(_options.PublicHttpsUrl, UriKind.Absolute, out Uri? uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return NotExpected();
        }

        string host = uri.Host;
        int port = uri.IsDefaultPort ? 443 : uri.Port;

        SslPolicyErrors observed = SslPolicyErrors.None;
        X509Certificate2? serverCert = null;

        try
        {
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(HandshakeTimeout);

            using TcpClient tcp = new();
            await tcp.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);

            await using SslStream ssl = new(tcp.GetStream(), leaveInnerStreamOpen: false, (_, cert, _, errors) =>
            {
                observed = errors;
                if (cert is not null)
                {
                    serverCert = new X509Certificate2(cert);
                }

                // Complete the handshake regardless so an invalid certificate can still be inspected and reported.
                return true;
            });

            await ssl.AuthenticateAsClientAsync(
                new SslClientAuthenticationOptions { TargetHost = host }, cts.Token).ConfigureAwait(false);

            bool present = !observed.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable) && serverCert is not null;
            return new TlsProbeFacts(
                HttpsExpected: true,
                Host: host,
                CertificatePresent: present,
                ChainValid: !observed.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors),
                HostnameMatches: !observed.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch),
                NotAfter: present && serverCert is not null ? new DateTimeOffset(serverCert.NotAfter.ToUniversalTime()) : null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // A connect/handshake fault (unreachable, timeout, reset) is a Fail check, not a thrown sweep. A caller
            // cancellation is rethrown; only our own timeout or a network fault becomes an error result.
            return new TlsProbeFacts(
                HttpsExpected: true, Host: host, CertificatePresent: false, ChainValid: false, HostnameMatches: false,
                NotAfter: null, Error: ex.Message);
        }
        finally
        {
            serverCert?.Dispose();
        }
    }

    private static TlsProbeFacts NotExpected() =>
        new(HttpsExpected: false, Host: null, CertificatePresent: false, ChainValid: false, HostnameMatches: false, NotAfter: null);
}
