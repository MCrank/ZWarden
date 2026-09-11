namespace ZWarden.Application.Authentication;

/// <summary>
/// The seam for authenticating a subject against an <b>external</b> identity provider (F4, ADR 0017).
/// It is an Application abstraction with <b>no concrete IdP SDK behind it in this layer</b> — the OIDC /
/// Auth0 implementation is F3B (v1.1); F4 proves the seam against a test double. A deployment with no
/// external IdP configured never registers an implementation, so ZWarden's architecture <i>supports</i>
/// an external IdP without <i>requiring</i> one (PRD 63). Mapping the returned identity to a local user
/// is Infrastructure's responsibility, keeping this interface Identity-free.
/// </summary>
public interface IExternalIdentityProvider
{
    /// <summary>The issuer this provider authenticates against (matches <see cref="ExternalIdentity.Issuer"/>).</summary>
    string Issuer { get; }

    /// <summary>Validates the supplied credential (an OIDC code/token, or the double's stand-in) and
    /// returns the asserted <see cref="ExternalIdentity"/>, or <see langword="null"/> if authentication
    /// failed. Never returns a local user — the caller maps the identity to one.</summary>
    Task<ExternalIdentity?> AuthenticateAsync(string credential, CancellationToken cancellationToken = default);
}
