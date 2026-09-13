using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Contracts.Tests.Protocol;

/// <summary>
/// F16 PR-A: the health wire surface. <see cref="ServerStateChanged"/> and <see cref="HealthChanged"/> are the
/// two reserved <see cref="AgentEvent"/> transition reports (AgentEvent.cs earmarks them "→ F16"), carrying the
/// observed run-state and the hierarchical <see cref="ServerHealth"/> rollup with its <see cref="HealthBreakdown"/>.
/// Health also rides the reconnect snapshot as an additive optional member on <see cref="ServerState"/>. Every
/// addition is additive (ADR 0020), so <see cref="ProtocolVersion.Current"/> does not move.
/// </summary>
public class HealthMessagesTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 13, 9, 0, 0, TimeSpan.Zero);

    private static readonly string[] ExpectedHealthNames =
        ["Stopped", "Starting", "Healthy", "Degraded", "Failed"];

    private static HealthBreakdown Breakdown() => new(
        Container: new ProbeCheck(ProbeStatus.Pass),
        Process: new ProbeCheck(ProbeStatus.Warn, "healthcheck reports starting"),
        Startup: new ProbeCheck(ProbeStatus.Pass),
        Network: new ProbeCheck(ProbeStatus.Fail, "query port 16262/udp unreachable"));

    [Test]
    public async Task ServerHealth_has_exactly_the_five_operator_states()
    {
        // The issue names five, and only five: stopped, starting, healthy, degraded, failed.
        string[] names = Enum.GetNames<ServerHealth>();

        await Assert.That(names).IsEquivalentTo(ExpectedHealthNames);
    }

    [Test]
    public async Task ServerStateChanged_round_trips_with_the_target_server_on_the_envelope()
    {
        ServerId server = ServerId.New();
        Envelope<ServerStateChanged> original = Envelope.Create(
            new ServerStateChanged(server, ServerRunState.Running), At, agentId: AgentId.New(), serverId: server);

        Envelope<ServerStateChanged> back = ProtocolJson.Deserialize<ServerStateChanged>(ProtocolJson.Serialize(original));

        await Assert.That(back.Payload).IsEqualTo(original.Payload);
        await Assert.That(back.Payload.ServerId).IsEqualTo(server);
    }

    [Test]
    public async Task ServerStateChanged_declares_its_discriminator()
    {
        string json = ProtocolJson.Serialize(Envelope.Create(
            new ServerStateChanged(ServerId.New(), ServerRunState.Stopping), At));

        await Assert.That(json).Contains("server.state-changed");
        await Assert.That(ProtocolJson.MessageTypes.ContainsKey("server.state-changed")).IsTrue();
    }

    [Test]
    public async Task HealthChanged_round_trips_its_rollup_and_breakdown()
    {
        ServerId server = ServerId.New();
        Envelope<HealthChanged> original = Envelope.Create(
            new HealthChanged(server, ServerHealth.Degraded, "query port unreachable", Breakdown()),
            At,
            agentId: AgentId.New(),
            serverId: server);

        Envelope<HealthChanged> back = ProtocolJson.Deserialize<HealthChanged>(ProtocolJson.Serialize(original));

        await Assert.That(back.Payload).IsEqualTo(original.Payload);
        await Assert.That(back.Payload.Breakdown.Network.Status).IsEqualTo(ProbeStatus.Fail);
    }

    [Test]
    public async Task HealthChanged_declares_its_discriminator()
    {
        string json = ProtocolJson.Serialize(Envelope.Create(
            new HealthChanged(ServerId.New(), ServerHealth.Healthy, "all probes pass", Breakdown()), At));

        await Assert.That(json).Contains("server.health-changed");
        await Assert.That(ProtocolJson.MessageTypes.ContainsKey("server.health-changed")).IsTrue();
    }

    [Test]
    public async Task Health_enums_serialize_by_name()
    {
        string json = ProtocolJson.Serialize(Envelope.Create(
            new HealthChanged(ServerId.New(), ServerHealth.Degraded, "impaired", Breakdown()), At));

        await Assert.That(json).Contains("Degraded");
        await Assert.That(json).Contains("Fail");
        await Assert.That(json).DoesNotContain("\"health\":3");
    }

    [Test]
    public async Task Snapshot_carries_health_when_present_and_defaults_it_absent()
    {
        // The added member is optional: an older Agent that omits it deserializes as null.
        ServerState withHealth = new(ServerId.New(), ServerRunState.Running, ServerHealth.Healthy);
        ServerState withoutHealth = new(ServerId.New(), ServerRunState.Stopped);

        await Assert.That(withoutHealth.Health).IsNull();

        AgentStateSnapshot snapshot = new([withHealth, withoutHealth]);
        Envelope<AgentStateSnapshot> back = ProtocolJson.Deserialize<AgentStateSnapshot>(
            ProtocolJson.Serialize(Envelope.Create(snapshot, At, agentId: AgentId.New())));

        await Assert.That(back.Payload.Servers[0].Health).IsEqualTo(ServerHealth.Healthy);
        await Assert.That(back.Payload.Servers[1].Health).IsNull();
    }

    [Test]
    public async Task The_health_additions_do_not_move_the_protocol_version()
    {
        // ADR 0020: new leaves and a new optional member are additive; the version stays put.
        await Assert.That(ProtocolVersionRange.Supported.Current).IsEqualTo(1);
    }
}
