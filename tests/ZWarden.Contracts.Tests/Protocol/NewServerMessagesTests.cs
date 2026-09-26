using System.Text.Json.Nodes;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Contracts.Tests.Protocol;

/// <summary>
/// #230: the new-server wizard's wire surface. <see cref="CreateServer"/> gains an optional per-server heap and the
/// initial settings seeded before first boot, <see cref="RecreateServer"/> gains an optional heap, and the Agent
/// reports its host's memory budget in a <see cref="HostCapacityReport"/>. All additive under ADR 0020 — no bump.
/// </summary>
public class NewServerMessagesTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 25, 20, 0, 0, TimeSpan.Zero);
    private const long GiB = 1024L * 1024 * 1024;

    [Test]
    public async Task CreateServer_round_trips_a_heap_and_initial_settings()
    {
        InitialServerSettings settings = new(
            Public: true, PublicName: "Friends of Knox", MaxPlayers: 12, Password: "hunter2", WelcomeMessage: "Hi!");
        Envelope<CreateServer> original = Envelope.Create(
            new CreateServer(GamePort: 27015, HeapSizeBytes: 6 * GiB, Settings: settings),
            At, agentId: AgentId.New(), serverId: ServerId.New(), operationId: OperationId.New());

        Envelope<CreateServer> back = ProtocolJson.Deserialize<CreateServer>(ProtocolJson.Serialize(original));

        await Assert.That(back.Payload.HeapSizeBytes).IsEqualTo(6 * GiB);
        await Assert.That(back.Payload.Settings).IsEqualTo(settings);
    }

    [Test]
    public async Task CreateServer_from_an_older_caller_has_no_heap_or_settings()
    {
        string json = ProtocolJson.Serialize(Envelope.Create(
            new CreateServer(GamePort: 27015), At, serverId: ServerId.New(), operationId: OperationId.New()));
        JsonNode node = JsonNode.Parse(json)!;
        node["payload"]!.AsObject().Remove("heapSizeBytes");
        node["payload"]!.AsObject().Remove("settings");

        Envelope<CreateServer> back = ProtocolJson.Deserialize<CreateServer>(node.ToJsonString());

        await Assert.That(back.Payload.GamePort).IsEqualTo(27015);
        await Assert.That(back.Payload.HeapSizeBytes).IsNull();
        await Assert.That(back.Payload.Settings).IsNull();
    }

    [Test]
    public async Task InitialServerSettings_never_prints_the_password()
    {
        InitialServerSettings settings = new(Public: null, PublicName: null, MaxPlayers: null, Password: "hunter2", WelcomeMessage: null);

        await Assert.That(settings.ToString()).DoesNotContain("hunter2");
        await Assert.That(new CreateServer(Settings: settings).ToString()).DoesNotContain("hunter2");
    }

    [Test]
    public async Task RecreateServer_round_trips_a_heap()
    {
        Envelope<RecreateServer> original = Envelope.Create(
            new RecreateServer(HeapSizeBytes: 8 * GiB), At, serverId: ServerId.New(), operationId: OperationId.New());

        Envelope<RecreateServer> back = ProtocolJson.Deserialize<RecreateServer>(ProtocolJson.Serialize(original));

        await Assert.That(back.Payload.HeapSizeBytes).IsEqualTo(8 * GiB);
        await Assert.That(back.Payload.GamePort).IsNull();
    }

    [Test]
    public async Task HostCapacityReport_round_trips_the_hosts_memory_budget()
    {
        HostCapacityReport report = new(
            TotalMemoryBytes: 32 * GiB, CommittedMemoryBytes: 10 * GiB, MemoryOverheadBytes: 6 * GiB,
            DefaultHeapSizeBytes: 4 * GiB, ReserveMemoryBytes: 2 * GiB);

        Envelope<HostCapacityReport> back = ProtocolJson.Deserialize<HostCapacityReport>(
            ProtocolJson.Serialize(Envelope.Create(report, At, agentId: AgentId.New())));

        await Assert.That(back.Payload).IsEqualTo(report);
    }

    [Test]
    public async Task HostCapacityReport_is_an_agent_event_with_its_own_discriminator()
    {
        string json = ProtocolJson.Serialize(Envelope.Create(new HostCapacityReport(1, 0, 0, 1, 0), At));

        await Assert.That(typeof(AgentEvent).IsAssignableFrom(typeof(HostCapacityReport))).IsTrue();
        await Assert.That(json).Contains("agent.host-capacity");
        await Assert.That(ProtocolJson.MessageTypes.ContainsKey("agent.host-capacity")).IsTrue();
        await Assert.That(ProtocolVersionRange.Supported.Current).IsEqualTo(1);
    }

    [Test]
    public async Task ProvisionResult_round_trips_the_heap_the_container_runs_with_and_an_older_agent_omits_it()
    {
        Envelope<OperationCompleted> original = Envelope.Create(
            new OperationCompleted(OperationOutcome.Succeeded, null, new ProvisionResult(16261, 16262, "c1", 6 * GiB)),
            At, serverId: ServerId.New(), operationId: OperationId.New());

        Envelope<OperationCompleted> back = ProtocolJson.Deserialize<OperationCompleted>(ProtocolJson.Serialize(original));

        await Assert.That(back.Payload.Provision!.HeapSizeBytes).IsEqualTo(6 * GiB);
        await Assert.That(new ProvisionResult(16261, 16262, "c1").HeapSizeBytes).IsNull();
    }
}
