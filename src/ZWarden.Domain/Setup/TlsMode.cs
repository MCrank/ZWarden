namespace ZWarden.Domain.Setup;

/// <summary>
/// The TLS deployment shape an operator declares during first-run setup (F33), mirroring the three
/// reference modes the ingress supports (PRD §46; ADR 0035). ZWarden <b>records</b> the declared mode so
/// the rest of the product and the F34 distribution can reason about it; it does not itself terminate TLS
/// or configure Caddy — that stays in the reference ingress (F32).
/// </summary>
public enum TlsMode
{
    /// <summary>A public DNS hostname with automatic Let's Encrypt certificates at the Caddy ingress.</summary>
    Public,

    /// <summary>An internal / air-gapped install using Caddy's built-in CA (<c>tls internal</c>).</summary>
    Private,

    /// <summary>The operator brings their own reverse proxy and certificate management; ZWarden.Web
    /// receives already-terminated traffic from a trusted upstream.</summary>
    ExistingReverseProxy,
}
