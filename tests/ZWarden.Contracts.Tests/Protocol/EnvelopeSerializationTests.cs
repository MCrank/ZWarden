using System.Text.Json;
using System.Text.Json.Nodes;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Contracts.Tests.Protocol;

/// <summary>
/// The serialization contract (F7 test plan 1-4, 8): every message round-trips through the one
/// canonical <see cref="ProtocolJson"/>, typed ids travel as canonical strings, the wire
/// discriminator dispatches to the right type, and additive changes stay compatible.
/// </summary>
public class EnvelopeSerializationTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 12, 8, 30, 0, TimeSpan.Zero);

    [Test]
    public async Task AgentHello_round_trips()
    {
        Envelope<AgentHello> original = Envelope.Create(new AgentHello(AgentId.New()), At);

        Envelope<AgentHello> back = ProtocolJson.Deserialize<AgentHello>(ProtocolJson.Serialize(original));

        await Assert.That(back.Payload).IsEqualTo(original.Payload);
    }

    [Test]
    public async Task AgentHeartbeat_round_trips()
    {
        Envelope<AgentHeartbeat> original = Envelope.Create(new AgentHeartbeat(AgentHealthStatus.Degraded), At);

        Envelope<AgentHeartbeat> back = ProtocolJson.Deserialize<AgentHeartbeat>(ProtocolJson.Serialize(original));

        await Assert.That(back.Payload).IsEqualTo(original.Payload);
    }

    [Test]
    public async Task OperationProgress_round_trips()
    {
        Envelope<OperationProgress> original = Envelope.Create(
            new OperationProgress(42, "installing"), At, operationId: OperationId.New());

        Envelope<OperationProgress> back = ProtocolJson.Deserialize<OperationProgress>(ProtocolJson.Serialize(original));

        await Assert.That(back.Payload).IsEqualTo(original.Payload);
    }

    [Test]
    public async Task OperationCompleted_round_trips()
    {
        Envelope<OperationCompleted> original = Envelope.Create(
            new OperationCompleted(OperationOutcome.Failed, "SteamCMD exited 8"), At, operationId: OperationId.New());

        Envelope<OperationCompleted> back = ProtocolJson.Deserialize<OperationCompleted>(ProtocolJson.Serialize(original));

        await Assert.That(back.Payload).IsEqualTo(original.Payload);
    }

    [Test]
    public async Task AgentStateSnapshot_round_trips_its_server_states()
    {
        AgentStateSnapshot snapshot = new([
            new ServerState(ServerId.New(), ServerRunState.Running),
            new ServerState(ServerId.New(), ServerRunState.Stopped),
        ]);
        Envelope<AgentStateSnapshot> original = Envelope.Create(snapshot, At, agentId: AgentId.New());

        Envelope<AgentStateSnapshot> back = ProtocolJson.Deserialize<AgentStateSnapshot>(ProtocolJson.Serialize(original));

        await Assert.That(back.Payload.Servers).IsEquivalentTo(original.Payload.Servers);
    }

    [Test]
    public async Task All_envelope_metadata_survives_the_round_trip()
    {
        Envelope<AgentHeartbeat> original = Envelope.Create(
            new AgentHeartbeat(AgentHealthStatus.Healthy),
            At,
            agentId: AgentId.New(),
            serverId: ServerId.New(),
            operationId: OperationId.New());

        Envelope<IProtocolMessage> back = ProtocolJson.Deserialize(ProtocolJson.Serialize(original));

        await Assert.That(back.ProtocolVersion).IsEqualTo(ProtocolVersion.Current);
        await Assert.That(back.MessageId).IsEqualTo(original.MessageId);
        await Assert.That(back.Timestamp).IsEqualTo(At);
        await Assert.That(back.Timestamp.Offset).IsEqualTo(TimeSpan.Zero);
        await Assert.That(back.AgentId).IsEqualTo(original.AgentId);
        await Assert.That(back.ServerId).IsEqualTo(original.ServerId);
        await Assert.That(back.OperationId).IsEqualTo(original.OperationId);
    }

    [Test]
    public async Task Typed_ids_are_written_as_canonical_prefixed_strings()
    {
        Envelope<AgentHello> original = Envelope.Create(
            new AgentHello(AgentId.New()), At, agentId: AgentId.New(), serverId: ServerId.New());

        string json = ProtocolJson.Serialize(original);

        await Assert.That(json).Contains($"\"{original.AgentId}\"");
        await Assert.That(json).Contains($"\"{original.ServerId}\"");
        await Assert.That(json).Contains($"\"{original.Payload.AgentId}\"");
    }

    [Test]
    public async Task A_bare_guid_where_a_typed_id_is_expected_fails_to_deserialize()
    {
        Envelope<AgentHello> original = Envelope.Create(new AgentHello(AgentId.New()), At);
        JsonObject wire = (JsonObject)JsonNode.Parse(ProtocolJson.Serialize(original))!;
        wire["messageId"] = Guid.CreateVersion7().ToString("D");

        await Assert.That(() => ProtocolJson.Deserialize(wire.ToJsonString())).Throws<JsonException>();
    }

    [Test]
    public async Task Deserialize_dispatches_to_the_concrete_payload_type()
    {
        string json = ProtocolJson.Serialize(Envelope.Create(new AgentHeartbeat(AgentHealthStatus.Healthy), At));

        Envelope<IProtocolMessage> back = ProtocolJson.Deserialize(json);

        await Assert.That(back.Payload).IsTypeOf<AgentHeartbeat>();
    }

    [Test]
    public async Task An_unknown_messageType_throws_naming_it()
    {
        JsonObject wire = (JsonObject)JsonNode.Parse(
            ProtocolJson.Serialize(Envelope.Create(new AgentHeartbeat(AgentHealthStatus.Healthy), At)))!;
        wire["messageType"] = "agent.does-not-exist";

        JsonException? caught = null;
        try
        {
            ProtocolJson.Deserialize(wire.ToJsonString());
        }
        catch (JsonException ex)
        {
            caught = ex;
        }

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Message).Contains("agent.does-not-exist");
    }

    [Test]
    public async Task Typed_deserialize_rejects_a_different_message_type()
    {
        string json = ProtocolJson.Serialize(Envelope.Create(new AgentHeartbeat(AgentHealthStatus.Healthy), At));

        await Assert.That(() => ProtocolJson.Deserialize<AgentHello>(json)).Throws<JsonException>();
    }

    [Test]
    public async Task Unknown_members_are_tolerated_for_additive_compatibility()
    {
        Envelope<OperationProgress> original = Envelope.Create(
            new OperationProgress(10, "starting"), At, operationId: OperationId.New());
        JsonObject wire = (JsonObject)JsonNode.Parse(ProtocolJson.Serialize(original))!;
        wire["futureEnvelopeField"] = "ignored";
        ((JsonObject)wire["payload"]!)["futurePayloadField"] = 99;

        Envelope<OperationProgress> back = ProtocolJson.Deserialize<OperationProgress>(wire.ToJsonString());

        await Assert.That(back.Payload).IsEqualTo(original.Payload);
    }

    [Test]
    public async Task Serialize_deserialize_is_identity_for_a_known_message()
    {
        Envelope<OperationCompleted> original = Envelope.Create(
            new OperationCompleted(OperationOutcome.Succeeded), At, operationId: OperationId.New());

        Envelope<OperationCompleted> back = ProtocolJson.Deserialize<OperationCompleted>(ProtocolJson.Serialize(original));

        await Assert.That(back).IsEqualTo(original);
    }

    [Test]
    public async Task Enums_are_serialized_by_name_not_ordinal()
    {
        string json = ProtocolJson.Serialize(Envelope.Create(new AgentHeartbeat(AgentHealthStatus.Degraded), At));

        await Assert.That(json).Contains("Degraded");
        await Assert.That(json).DoesNotContain("\"health\":1");
    }
}
