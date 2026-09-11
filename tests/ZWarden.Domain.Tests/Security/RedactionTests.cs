using ZWarden.Domain.Security;

namespace ZWarden.Domain.Tests.Security;

/// <summary>S1: the redaction primitives F29/F30 reuse.</summary>
public class RedactionTests
{
    [Test]
    public async Task MaskAll_replaces_the_whole_value()
    {
        await Assert.That(Redaction.MaskAll("anything")).IsEqualTo("***");
        await Assert.That(Redaction.MaskAll(null)).IsEqualTo("***");
    }

    [Test]
    public async Task KeepLast_reveals_only_the_tail()
    {
        await Assert.That(Redaction.KeepLast("sk-1234567890", 4)).IsEqualTo("***7890");
    }

    [Test]
    public async Task KeepLast_masks_a_value_no_longer_than_the_window()
    {
        await Assert.That(Redaction.KeepLast("1234", 4)).IsEqualTo("***");
        await Assert.That(Redaction.KeepLast("12", 4)).IsEqualTo("***");
        await Assert.That(Redaction.KeepLast(null, 4)).IsEqualTo("***");
    }

    [Test]
    [Arguments("Password")]
    [Arguments("RconPassword")]
    [Arguments("DbConnectionString")]
    [Arguments("Authorization")]
    [Arguments("api_key")]
    public async Task IsSensitiveKey_matches_known_tokens_case_insensitively(string key)
    {
        await Assert.That(Redaction.IsSensitiveKey(key)).IsTrue();
    }

    [Test]
    [Arguments("Name")]
    [Arguments("ServerId")]
    [Arguments("Region")]
    public async Task IsSensitiveKey_passes_benign_keys(string key)
    {
        await Assert.That(Redaction.IsSensitiveKey(key)).IsFalse();
    }

    [Test]
    public async Task RedactKnownKeys_masks_only_sensitive_fields()
    {
        Dictionary<string, string?> fields = new()
        {
            ["Name"] = "west-1",
            ["RconPassword"] = "hunter2",
            ["ConnectionString"] = "Host=db;Password=p",
        };

        IReadOnlyDictionary<string, string?> redacted = Redaction.RedactKnownKeys(fields);

        await Assert.That(redacted["Name"]).IsEqualTo("west-1");
        await Assert.That(redacted["RconPassword"]).IsEqualTo("***");
        await Assert.That(redacted["ConnectionString"]).IsEqualTo("***");
    }

    [Test]
    public async Task KeepLast_rejects_a_negative_window()
    {
        await Assert.That(() => Redaction.KeepLast("abc", -1)).Throws<ArgumentOutOfRangeException>();
    }
}
