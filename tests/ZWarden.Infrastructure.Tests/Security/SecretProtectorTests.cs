using System.Text;
using ZWarden.Domain.Security;
using ZWarden.Infrastructure.Security;

namespace ZWarden.Infrastructure.Tests.Security;

/// <summary>S3: authenticated encryption round-trips, detects tampering, and rotates (ADR 0015).</summary>
public class SecretProtectorTests
{
    private static byte[] NewKey(byte fill)
    {
        byte[] key = new byte[KeyRing.KeySizeBytes];
        Array.Fill(key, fill);
        return key;
    }

    private static KeyRing Ring(string active, params (string Id, byte Fill)[] keys) =>
        new(keys.ToDictionary(k => k.Id, k => NewKey(k.Fill), StringComparer.Ordinal), active);

    private static SecretProtector Protector(string active, params (string Id, byte Fill)[] keys) =>
        new(Ring(active, keys));

    [Test]
    [Arguments("")]
    [Arguments("hunter2")]
    [Arguments("a longer secret value with unicode: éü☃")]
    public async Task Round_trips_a_value(string plaintext)
    {
        SecretProtector protector = Protector("k1", ("k1", 1));

        string envelope = protector.ProtectString(plaintext);
        await Assert.That(protector.UnprotectString(envelope)).IsEqualTo(plaintext);
    }

    [Test]
    public async Task Round_trips_binary_of_various_lengths()
    {
        SecretProtector protector = Protector("k1", ("k1", 1));
        foreach (int length in new[] { 0, 1, 15, 16, 17, 1024 })
        {
            byte[] payload = new byte[length];
            Random.Shared.NextBytes(payload);

            string envelope = protector.Protect(payload);
            await Assert.That(protector.Unprotect(envelope)).IsEquivalentTo(payload);
        }
    }

    [Test]
    public async Task Same_plaintext_yields_distinct_envelopes()
    {
        SecretProtector protector = Protector("k1", ("k1", 1));

        string first = protector.ProtectString("same");
        string second = protector.ProtectString("same");

        await Assert.That(first).IsNotEqualTo(second);
        await Assert.That(protector.UnprotectString(first)).IsEqualTo("same");
        await Assert.That(protector.UnprotectString(second)).IsEqualTo("same");
    }

    [Test]
    public async Task Flipping_any_single_byte_makes_unprotect_throw()
    {
        SecretProtector protector = Protector("k1", ("k1", 1));
        byte[] envelope = Convert.FromBase64String(protector.ProtectString("tamper-me"));

        for (int i = 0; i < envelope.Length; i++)
        {
            byte[] mutated = (byte[])envelope.Clone();
            mutated[i] ^= 0xFF;
            string candidate = Convert.ToBase64String(mutated);

            await Assert.That(() => protector.Unprotect(candidate))
                .Throws<SecretProtectionException>();
        }
    }

    [Test]
    public async Task An_unknown_key_id_is_reported_as_such()
    {
        SecretProtector writer = Protector("k1", ("k1", 1));
        string envelope = writer.ProtectString("secret");

        SecretProtector readerWithoutK1 = Protector("k2", ("k2", 2));

        SecretProtectionException? ex = await Assert.That(() => readerWithoutK1.Unprotect(envelope))
            .Throws<SecretProtectionException>();
        await Assert.That(ex!.Failure).IsEqualTo(SecretProtectionFailure.UnknownKey);
    }

    [Test]
    public async Task Malformed_base64_is_reported_as_malformed()
    {
        SecretProtector protector = Protector("k1", ("k1", 1));

        SecretProtectionException? ex = await Assert.That(() => protector.Unprotect("not base64!!"))
            .Throws<SecretProtectionException>();
        await Assert.That(ex!.Failure).IsEqualTo(SecretProtectionFailure.Malformed);
    }

    [Test]
    public async Task Rotation_encrypts_under_the_active_key_and_still_decrypts_old_values()
    {
        // A value written when k1 was active.
        SecretProtector before = Protector("k1", ("k1", 1));
        string oldEnvelope = before.ProtectString("legacy");

        // Rotate: k2 is now active, k1 retained for decryption.
        SecretProtector after = Protector("k2", ("k2", 2), ("k1", 1));

        await Assert.That(after.UnprotectString(oldEnvelope)).IsEqualTo("legacy");

        // New values use k2 - decodable only where k2 is present.
        string newEnvelope = after.ProtectString("current");
        SecretProtector onlyK1 = Protector("k1", ("k1", 1));
        await Assert.That(() => onlyK1.Unprotect(newEnvelope)).Throws<SecretProtectionException>();
    }

    [Test]
    public async Task Dropping_a_retired_key_orphans_its_values()
    {
        SecretProtector before = Protector("k1", ("k1", 1));
        string envelope = before.ProtectString("orphan");

        // k1 removed from the ring entirely.
        SecretProtector onlyK2 = Protector("k2", ("k2", 2));

        SecretProtectionException? ex = await Assert.That(() => onlyK2.Unprotect(envelope))
            .Throws<SecretProtectionException>();
        await Assert.That(ex!.Failure).IsEqualTo(SecretProtectionFailure.UnknownKey);
    }

    [Test]
    public async Task Envelope_header_advertises_version_and_key_id_without_decrypting()
    {
        SecretProtector protector = Protector("k1", ("k1", 1));
        byte[] envelope = Convert.FromBase64String(protector.ProtectString("x"));

        await Assert.That(envelope[0]).IsEqualTo((byte)1); // version
        int keyIdLength = envelope[1];
        string keyId = Encoding.UTF8.GetString(envelope, 2, keyIdLength);
        await Assert.That(keyId).IsEqualTo("k1");
    }
}
