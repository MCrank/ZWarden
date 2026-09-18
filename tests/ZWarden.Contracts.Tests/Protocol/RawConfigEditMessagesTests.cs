using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;

namespace ZWarden.Contracts.Tests.Protocol;

/// <summary>
/// F20c PR-D (ADR 0042): the raw whole-file configuration-edit wire surface. <see cref="ConfigApplyRaw"/> is an
/// additive <see cref="AgentCommand"/> that carries only the file, the drift baseline, and a correlation id — never
/// the operator's text, which is staged separately, so the closed-command vocabulary holds. The
/// <see cref="ServerConfigRawEditCodec"/> chunks the operator's text for the trip up to the Agent and reassembles
/// it in any arrival order while refusing an incomplete or oversized set. Its channel name
/// (<see cref="AgentHubProtocol.StageServerConfigRawEdit"/>) is transport plumbing, not a message.
/// </summary>
public class RawConfigEditMessagesTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task ConfigApplyRaw_round_trips_and_declares_its_discriminator()
    {
        ConfigApplyRaw command = new(PzConfigFile.SandboxVars, BaselineHash: "abc123", CorrelationId: "corr-1");
        Envelope<ConfigApplyRaw> original = Envelope.Create<ConfigApplyRaw>(
            command, At, agentId: AgentId.New(), serverId: ServerId.New(), operationId: OperationId.New());

        string json = ProtocolJson.Serialize(original);
        Envelope<IProtocolMessage> back = ProtocolJson.Deserialize(json);

        await Assert.That(json).Contains("configuration.apply-raw");
        await Assert.That(ProtocolJson.MessageTypes.ContainsKey("configuration.apply-raw")).IsTrue();
        await Assert.That(back.Payload).IsEqualTo(command);
    }

    [Test]
    public async Task ConfigApplyRaw_is_an_agent_command_and_never_carries_the_file_text()
    {
        // The whole-file text is staged over a separate channel; the command carries only file/baseline/correlation,
        // so no AgentCommand carries a free-form string (trust-boundaries.md §9 rule 3).
        await Assert.That(typeof(AgentCommand).IsAssignableFrom(typeof(ConfigApplyRaw))).IsTrue();
        await Assert.That(typeof(AgentEvent).IsAssignableFrom(typeof(ConfigApplyRaw))).IsFalse();

        string[] properties = [.. typeof(ConfigApplyRaw).GetProperties().Select(p => p.Name)];
        await Assert.That(properties).Contains("CorrelationId");
        await Assert.That(properties).DoesNotContain("Content");
        await Assert.That(properties).DoesNotContain("Text");
        await Assert.That(properties).DoesNotContain("RawText");
    }

    [Test]
    public async Task Raw_text_survives_encode_then_decode()
    {
        const string raw = "VERSION = 1,\nSandboxVars = {\n    Zombies = 2,\n    -- a comment with <BHC> markup\n}\n";

        string back = ServerConfigRawEditCodec.Decode(ServerConfigRawEditCodec.Encode("corr-1", raw));

        await Assert.That(back).IsEqualTo(raw);
    }

    [Test]
    public async Task Empty_text_yields_one_chunk_and_round_trips()
    {
        IReadOnlyList<ServerConfigRawEditChunk> chunks = ServerConfigRawEditCodec.Encode("corr-e", string.Empty);

        await Assert.That(chunks.Count).IsEqualTo(1);
        await Assert.That(ServerConfigRawEditCodec.Decode(chunks)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task A_large_edit_splits_into_multiple_ordered_chunks()
    {
        string raw = new('x', 100_000);

        IReadOnlyList<ServerConfigRawEditChunk> chunks = ServerConfigRawEditCodec.Encode("corr-2", raw);

        await Assert.That(chunks.Count).IsGreaterThan(1);
        for (int i = 0; i < chunks.Count; i++)
        {
            await Assert.That(chunks[i].ChunkIndex).IsEqualTo(i);
            await Assert.That(chunks[i].ChunkCount).IsEqualTo(chunks.Count);
            await Assert.That(chunks[i].CorrelationId).IsEqualTo("corr-2");
        }

        await Assert.That(ServerConfigRawEditCodec.Decode(chunks)).IsEqualTo(raw);
    }

    [Test]
    public async Task Decode_reassembles_chunks_that_arrive_out_of_order()
    {
        string raw = new('y', 80_000);
        List<ServerConfigRawEditChunk> shuffled = [.. ServerConfigRawEditCodec.Encode("corr-3", raw)];
        shuffled.Reverse();

        await Assert.That(ServerConfigRawEditCodec.Decode(shuffled)).IsEqualTo(raw);
    }

    [Test]
    public async Task Decode_refuses_an_incomplete_chunk_set()
    {
        List<ServerConfigRawEditChunk> chunks = [.. ServerConfigRawEditCodec.Encode("corr-4", new string('z', 80_000))];
        chunks.RemoveAt(chunks.Count - 1);

        await Assert.That(() => ServerConfigRawEditCodec.Decode(chunks)).Throws<FormatException>();
    }

    [Test]
    public async Task Decode_refuses_a_set_larger_than_the_chunk_cap()
    {
        // A sender that claims more chunks than the cap is refused before any reassembly (bounds the buffer).
        int oversized = ServerConfigRawEditCodec.MaxChunkCount + 1;
        List<ServerConfigRawEditChunk> chunks =
            [.. Enumerable.Range(0, oversized).Select(i => new ServerConfigRawEditChunk("corr-6", i, oversized, "AA=="))];

        await Assert.That(() => ServerConfigRawEditCodec.Decode(chunks)).Throws<FormatException>();
    }
}
