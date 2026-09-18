using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;

namespace ZWarden.Contracts.Tests.Protocol;

/// <summary>
/// F20c (ADR 0041): the live-configuration-read wire surface. <see cref="ServerConfigContent"/> is an additive
/// <see cref="AgentEvent"/> chunk that round-trips through the one canonical <see cref="ProtocolJson"/> and does not
/// bump the protocol version; the <see cref="ServerConfigContentCodec"/> splits a <see cref="ConfigReadPayload"/>
/// into chunks and reassembles it — in any arrival order — while refusing an incomplete or oversized set. The
/// request name it rides alongside (<see cref="AgentHubProtocol.RequestServerConfigRead"/>) is transport plumbing,
/// not a message.
/// </summary>
public class ConfigReadMessagesTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    private static ConfigReadPayload SamplePayload(ServerId server, string rawText) =>
        new(
            server,
            PzConfigFile.SandboxVars,
            ConfigReadStatus.Read,
            [
                new ConfigSettingValue("Map.AllowMiniMap", ConfigValueKind.Bool, "false", "Allow the in-game minimap.\n<BHC>ignored"),
                new ConfigSettingValue("Zombies.Speed", ConfigValueKind.Number, "2", "1 = Sprinters\n2 = Fast Shamblers"),
                new ConfigSettingValue("ServerName", ConfigValueKind.Text, "My Server", null),
            ],
            rawText,
            "abc123",
            [new ConfigReadDiagnostic("A key with no schema entry was preserved.", null, null)]);

    [Test]
    public async Task ServerConfigContent_round_trips_through_the_canonical_serializer()
    {
        ServerId server = ServerId.New();
        ServerConfigContent chunk = new("corr-1", ChunkIndex: 2, ChunkCount: 5, Chunk: "c29tZS1ib2R5");
        Envelope<ServerConfigContent> original = Envelope.Create(chunk, At, agentId: AgentId.New(), serverId: server);

        Envelope<ServerConfigContent> back =
            ProtocolJson.Deserialize<ServerConfigContent>(ProtocolJson.Serialize(original));

        await Assert.That(back.Payload).IsEqualTo(chunk);
        await Assert.That(back.ServerId).IsEqualTo(server);
    }

    [Test]
    public async Task ServerConfigContent_declares_its_discriminator_and_stays_additive()
    {
        string json = ProtocolJson.Serialize(
            Envelope.Create(new ServerConfigContent("c", 0, 1, ""), At));

        await Assert.That(json).Contains("configuration.read-content");
        await Assert.That(ProtocolJson.MessageTypes.ContainsKey("configuration.read-content")).IsTrue();
        await Assert.That(ProtocolVersionRange.Supported.Current).IsEqualTo(1);
    }

    [Test]
    public async Task ServerConfigContent_is_an_agent_event_never_a_command()
    {
        // The read reply is an observed report (Agent → Web), so it must be an AgentEvent — never an AgentCommand,
        // which is the operation-dispatch vocabulary. Its request rides a transport constant, not a message type.
        await Assert.That(typeof(AgentEvent).IsAssignableFrom(typeof(ServerConfigContent))).IsTrue();
        await Assert.That(typeof(AgentCommand).IsAssignableFrom(typeof(ServerConfigContent))).IsFalse();
    }

    [Test]
    public async Task A_config_read_payload_survives_encode_then_decode()
    {
        ServerId server = ServerId.New();
        ConfigReadPayload payload = SamplePayload(server, "SandboxVars = {\n    Zombies = 2,\n}\n");

        ConfigReadPayload back = ServerConfigContentCodec.Decode(ServerConfigContentCodec.Encode("corr-1", payload));

        await Assert.That(back.ServerId).IsEqualTo(server);
        await Assert.That(back.File).IsEqualTo(PzConfigFile.SandboxVars);
        await Assert.That(back.Status).IsEqualTo(ConfigReadStatus.Read);
        await Assert.That(back.RawText).IsEqualTo(payload.RawText);
        await Assert.That(back.BaselineHash).IsEqualTo("abc123");
        await Assert.That(back.Settings).IsEquivalentTo(payload.Settings);
        await Assert.That(back.Diagnostics).IsEquivalentTo(payload.Diagnostics);
    }

    [Test]
    public async Task A_large_payload_splits_into_multiple_ordered_chunks()
    {
        // A raw text well over one chunk forces several sequenced sends that reassemble to the original.
        ConfigReadPayload payload = SamplePayload(ServerId.New(), new string('x', 100_000));

        IReadOnlyList<ServerConfigContent> chunks = ServerConfigContentCodec.Encode("corr-2", payload);

        await Assert.That(chunks.Count).IsGreaterThan(1);
        for (int i = 0; i < chunks.Count; i++)
        {
            await Assert.That(chunks[i].ChunkIndex).IsEqualTo(i);
            await Assert.That(chunks[i].ChunkCount).IsEqualTo(chunks.Count);
            await Assert.That(chunks[i].CorrelationId).IsEqualTo("corr-2");
        }

        ConfigReadPayload back = ServerConfigContentCodec.Decode(chunks);
        await Assert.That(back.RawText).IsEqualTo(payload.RawText);
    }

    [Test]
    public async Task Decode_reassembles_chunks_that_arrive_out_of_order()
    {
        ConfigReadPayload payload = SamplePayload(ServerId.New(), new string('y', 80_000));
        List<ServerConfigContent> shuffled = [.. ServerConfigContentCodec.Encode("corr-3", payload)];
        shuffled.Reverse();

        ConfigReadPayload back = ServerConfigContentCodec.Decode(shuffled);

        await Assert.That(back.RawText).IsEqualTo(payload.RawText);
    }

    [Test]
    public async Task Decode_refuses_an_incomplete_chunk_set()
    {
        ConfigReadPayload payload = SamplePayload(ServerId.New(), new string('z', 80_000));
        List<ServerConfigContent> chunks = [.. ServerConfigContentCodec.Encode("corr-4", payload)];
        chunks.RemoveAt(chunks.Count - 1); // drop the last chunk

        await Assert.That(() => ServerConfigContentCodec.Decode(chunks)).Throws<FormatException>();
    }

    [Test]
    public async Task Encode_carries_a_missing_or_unparsed_file_as_a_status()
    {
        // A missing file is a first-class status, not an exception: it round-trips with empty settings/raw text.
        ConfigReadPayload missing = new(
            ServerId.New(), PzConfigFile.Ini, ConfigReadStatus.FileMissing, [], string.Empty, null, []);

        ConfigReadPayload back = ServerConfigContentCodec.Decode(ServerConfigContentCodec.Encode("corr-5", missing));

        await Assert.That(back.Status).IsEqualTo(ConfigReadStatus.FileMissing);
        await Assert.That(back.Settings).IsEmpty();
        await Assert.That(back.BaselineHash).IsNull();
    }
}
