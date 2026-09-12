using ZWarden.Domain.Security;
using ZWarden.Infrastructure.Security;

namespace ZWarden.Infrastructure.Tests.Security;

/// <summary>
/// F9 S2 (PR 1): the credential hasher mints full-entropy bearer secrets and hashes them one-way
/// (decision 2, hash-only; ADR 0007). Both the one-time enrollment secret and the per-Agent credential
/// run through it — the raw secret is shown once and never stored; only the hash is persisted.
/// </summary>
public class CredentialHasherTests
{
    private static readonly CredentialHasher Hasher = new();

    [Test]
    public async Task Generated_secrets_carry_the_prefix_and_are_high_entropy()
    {
        SecretString secret = Hasher.Generate("zwe");
        string value = secret.Reveal();

        await Assert.That(value.StartsWith("zwe_", StringComparison.Ordinal)).IsTrue();
        // 32 random bytes, base64url (no padding) => 43 chars, after the "zwe_" prefix.
        await Assert.That(value.Length).IsGreaterThanOrEqualTo("zwe_".Length + 43);
    }

    [Test]
    public async Task Generated_secrets_are_distinct()
    {
        await Assert.That(Hasher.Generate("zwa").Reveal()).IsNotEqualTo(Hasher.Generate("zwa").Reveal());
    }

    [Test]
    public async Task Hash_is_stable_for_the_same_secret()
    {
        SecretString secret = Hasher.Generate("zwe");
        await Assert.That(Hasher.Hash(secret)).IsEqualTo(Hasher.Hash(secret));
    }

    [Test]
    public async Task Hash_is_one_way_and_not_the_secret()
    {
        SecretString secret = Hasher.Generate("zwe");
        string hash = Hasher.Hash(secret);

        await Assert.That(hash).IsNotEqualTo(secret.Reveal());
        await Assert.That(hash.Length).IsEqualTo(64); // SHA-256, lowercase hex.
    }

    [Test]
    public async Task Different_secrets_hash_differently()
    {
        await Assert.That(Hasher.Hash(Hasher.Generate("zwe")))
            .IsNotEqualTo(Hasher.Hash(Hasher.Generate("zwe")));
    }

    [Test]
    public async Task Generate_rejects_a_blank_prefix()
    {
        await Assert.That(() => Hasher.Generate(" ")).Throws<ArgumentException>();
    }
}
