using ZWarden.Infrastructure.Security;

namespace ZWarden.Infrastructure.Tests.Security;

/// <summary>S2: the key-ring loader parses configuration and fails closed (ADR 0015).</summary>
public class KeyRingLoaderTests
{
    private static string Key(byte fill) => Convert.ToBase64String(NewKey(fill));

    private static byte[] NewKey(byte fill)
    {
        byte[] key = new byte[KeyRing.KeySizeBytes];
        Array.Fill(key, fill);
        return key;
    }

    [Test]
    public async Task Loads_a_valid_single_key_ring()
    {
        KeyRing ring = KeyRingLoader.Load($"k1:{Key(1)}", "k1");

        await Assert.That(ring.ActiveKeyId).IsEqualTo("k1");
        await Assert.That(ring.Contains("k1")).IsTrue();
        await Assert.That(ring.GetKey("k1").ToArray()).IsEquivalentTo(NewKey(1));
    }

    [Test]
    public async Task Loads_multiple_keys_and_keeps_retired_ones_for_decryption()
    {
        KeyRing ring = KeyRingLoader.Load($"k1:{Key(1)};k2:{Key(2)}", "k2");

        await Assert.That(ring.ActiveKeyId).IsEqualTo("k2");
        await Assert.That(ring.Contains("k1")).IsTrue();
        await Assert.That(ring.Contains("k2")).IsTrue();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    public async Task Fails_closed_when_no_keys_are_configured(string? keysValue)
    {
        await Assert.That(() => KeyRingLoader.Load(keysValue, "k1"))
            .Throws<KeyRingConfigurationException>();
    }

    [Test]
    public async Task Fails_closed_when_no_active_key_id_is_configured()
    {
        await Assert.That(() => KeyRingLoader.Load($"k1:{Key(1)}", null))
            .Throws<KeyRingConfigurationException>();
    }

    [Test]
    public async Task Fails_closed_when_the_active_key_is_not_in_the_ring()
    {
        await Assert.That(() => KeyRingLoader.Load($"k1:{Key(1)}", "missing"))
            .Throws<KeyRingConfigurationException>();
    }

    [Test]
    public async Task Fails_closed_on_a_short_key()
    {
        string shortKey = Convert.ToBase64String(new byte[16]);
        await Assert.That(() => KeyRingLoader.Load($"k1:{shortKey}", "k1"))
            .Throws<KeyRingConfigurationException>();
    }

    [Test]
    public async Task Fails_closed_on_unparseable_base64()
    {
        await Assert.That(() => KeyRingLoader.Load("k1:not-base64!!", "k1"))
            .Throws<KeyRingConfigurationException>();
    }

    [Test]
    public async Task Fails_closed_on_a_malformed_entry()
    {
        await Assert.That(() => KeyRingLoader.Load("k1-no-colon", "k1"))
            .Throws<KeyRingConfigurationException>();
    }

    [Test]
    public async Task Fails_closed_on_a_duplicate_key_id()
    {
        await Assert.That(() => KeyRingLoader.Load($"k1:{Key(1)};k1:{Key(2)}", "k1"))
            .Throws<KeyRingConfigurationException>();
    }

    [Test]
    public async Task Fails_closed_on_an_invalid_key_id()
    {
        await Assert.That(() => KeyRingLoader.Load($"bad id:{Key(1)}", "bad id"))
            .Throws<KeyRingConfigurationException>();
    }

    [Test]
    public async Task A_fail_closed_message_never_contains_key_bytes()
    {
        string secretKey = Key(7);
        try
        {
            KeyRingLoader.Load($"k1:{secretKey}", "missing");
        }
        catch (KeyRingConfigurationException ex)
        {
            await Assert.That(ex.Message).DoesNotContain(secretKey);
            return;
        }

        Assert.Fail("Expected a KeyRingConfigurationException.");
    }
}
