using ZWarden.Domain.Security;

namespace ZWarden.Application.Agents;

/// <summary>
/// Mints and one-way-hashes the bearer secrets of the trust flow (decision 2, hash-only; ADR 0007): the
/// one-time enrollment secret and the per-Agent credential. A generated secret is full-entropy random and
/// carries a prefix (for secret scanners); only its <see cref="Hash"/> is ever persisted, so a database
/// compromise yields hashes — not usable credentials. The implementation lives in
/// <c>ZWarden.Infrastructure.Security</c>, where raw cryptographic primitives are confined.
/// </summary>
public interface ICredentialHasher
{
    /// <summary>Mints a fresh full-entropy secret, prefixed (e.g. <c>zwe_…</c> / <c>zwa_…</c>). Shown once.</summary>
    SecretString Generate(string prefix);

    /// <summary>The stable one-way hash of <paramref name="secret"/> — the only form ever stored.</summary>
    string Hash(SecretString secret);
}
