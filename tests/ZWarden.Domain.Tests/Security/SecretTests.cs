using System.Text.Json;
using ZWarden.Domain.Security;

namespace ZWarden.Domain.Tests.Security;

/// <summary>S1: secret-aware types never stringify their contents (PRD 10 exit condition).</summary>
public class SecretTests
{
    [Test]
    public async Task SecretString_reveals_only_through_Reveal()
    {
        SecretString secret = new("hunter2");
        await Assert.That(secret.HasValue).IsTrue();
        await Assert.That(secret.Reveal()).IsEqualTo("hunter2");
    }

    [Test]
    public async Task SecretString_ToString_and_interpolation_are_redacted()
    {
        SecretString secret = new("hunter2");
        await Assert.That(secret.ToString()).IsEqualTo("***");
        await Assert.That($"pw={secret}").IsEqualTo("pw=***");
        await Assert.That($"pw={secret}").DoesNotContain("hunter2");
    }

    [Test]
    public async Task SecretString_serializes_as_the_marker_and_refuses_to_deserialize()
    {
        SecretString secret = new("hunter2");
        string json = JsonSerializer.Serialize(secret);
        await Assert.That(json).IsEqualTo("\"***\"");
        await Assert.That(json).DoesNotContain("hunter2");

        await Assert.That(() => JsonSerializer.Deserialize<SecretString>("\"hunter2\""))
            .Throws<NotSupportedException>();
    }

    [Test]
    public async Task SecretString_serialized_inside_an_object_hides_the_value()
    {
        var holder = new { User = "admin", Password = new SecretString("hunter2") };
        string json = JsonSerializer.Serialize(holder);
        await Assert.That(json).DoesNotContain("hunter2");
        await Assert.That(json).Contains("***");
    }

    [Test]
    public async Task Default_SecretString_has_no_value_and_reveal_throws()
    {
        SecretString empty = default;
        await Assert.That(empty.HasValue).IsFalse();
        await Assert.That(() => empty.Reveal()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Generic_Secret_reveals_and_redacts()
    {
        Secret<byte[]> secret = new([1, 2, 3]);
        await Assert.That(secret.Reveal()).IsEquivalentTo(new byte[] { 1, 2, 3 });
        await Assert.That(secret.ToString()).IsEqualTo("***");
    }

    [Test]
    public async Task Generic_Secret_serializes_as_the_marker()
    {
        Secret<byte[]> secret = new([1, 2, 3]);
        string json = JsonSerializer.Serialize(secret);
        await Assert.That(json).IsEqualTo("\"***\"");
    }

    [Test]
    public async Task Constructing_a_secret_from_null_throws()
    {
        await Assert.That(() => new SecretString(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => new Secret<string>(null!)).Throws<ArgumentNullException>();
    }
}
