using System.Text.Json;
using ZWarden.Domain.Ids;

namespace ZWarden.Domain.Tests.Ids;

/// <summary>The System.Text.Json converter emits/reads the canonical string (S3, PRD 6).</summary>
public class TypedIdJsonTests
{
    private static readonly JsonSerializerOptions Options =
        new() { Converters = { new TypedIdJsonConverterFactory() } };

    private sealed record Envelope(AgentId Agent, ServerId Server);

    [Test]
    public async Task Round_trips_typed_ids_as_canonical_strings()
    {
        Envelope original = new(AgentId.New(), ServerId.New());

        string json = JsonSerializer.Serialize(original, Options);
        await Assert.That(json).Contains($"\"{original.Agent}\"");
        await Assert.That(json).Contains($"\"{original.Server}\"");

        Envelope? back = JsonSerializer.Deserialize<Envelope>(json, Options);
        await Assert.That(back).IsEqualTo(original);
    }

    [Test]
    public async Task A_bare_guid_without_the_prefix_fails_to_deserialize()
    {
        string json = $"{{\"Agent\":\"{Guid.CreateVersion7():D}\",\"Server\":\"{ServerId.New()}\"}}";
        await Assert.That(() => JsonSerializer.Deserialize<Envelope>(json, Options)).Throws<JsonException>();
    }

    [Test]
    public async Task The_wrong_prefix_fails_to_deserialize()
    {
        string json = $"{{\"Agent\":\"usr-{Guid.CreateVersion7():D}\",\"Server\":\"{ServerId.New()}\"}}";
        await Assert.That(() => JsonSerializer.Deserialize<Envelope>(json, Options)).Throws<JsonException>();
    }
}
