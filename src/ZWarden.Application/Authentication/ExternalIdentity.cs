namespace ZWarden.Application.Authentication;

/// <summary>
/// The identity an external identity provider asserts for a subject (F4): the issuer, the provider's
/// stable subject identifier, and an optional email. It is a plain Domain-level value — no Identity or
/// provider-SDK type — so the seam that produces it (<see cref="IExternalIdentityProvider"/>) keeps
/// Application free of any concrete IdP dependency (PRD 63, arch rule 6). Mapping it to a local user is
/// Infrastructure's job.
/// </summary>
/// <param name="Issuer">The identity provider that asserted this identity (used as the login provider key).</param>
/// <param name="Subject">The provider's stable, unique identifier for the subject (the <c>sub</c> claim).</param>
/// <param name="Email">The subject's email, if the provider asserts one.</param>
public sealed record ExternalIdentity(string Issuer, string Subject, string? Email);
