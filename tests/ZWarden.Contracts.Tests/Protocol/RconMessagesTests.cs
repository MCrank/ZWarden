using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Contracts.Tests.Protocol;

/// <summary>
/// F18: the RCON health-probe wire surface. <see cref="ProbeRconHealth"/> is a payload-free leaf of the
/// closed <see cref="AgentCommand"/> vocabulary (the target Server rides the envelope, like
/// <see cref="UpdateServer"/>), declaring a unique <c>diagnostics.*</c> discriminator; the completion carries
/// an optional <see cref="RconHealthResult"/> that is additive (ADR 0020) and round-trips through the one
/// canonical <see cref="ProtocolJson"/>. The change is additive, so <see cref="ProtocolVersion.Current"/>
/// stays 1.
/// </summary>
public class RconMessagesTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 13, 8, 30, 0, TimeSpan.Zero);

    [Test]
    public async Task ProbeRconHealth_round_trips_with_the_target_server_on_the_envelope()
    {
        ServerId server = ServerId.New();
        AgentCommand command = new ProbeRconHealth();
        Envelope<AgentCommand> original = Envelope.Create(
            command, At, agentId: AgentId.New(), serverId: server, operationId: OperationId.New());

        Envelope<IProtocolMessage> back = ProtocolJson.Deserialize(ProtocolJson.Serialize(original));

        await Assert.That(back.ServerId).IsEqualTo(server);
        await Assert.That(back.Payload).IsEqualTo((IProtocolMessage)command);
    }

    [Test]
    public async Task ProbeRconHealth_declares_the_diagnostics_discriminator()
    {
        ProtocolMessageAttribute? attribute = (ProtocolMessageAttribute?)Attribute.GetCustomAttribute(
            typeof(ProbeRconHealth), typeof(ProtocolMessageAttribute));

        await Assert.That(attribute).IsNotNull();
        await Assert.That(attribute!.Discriminator).IsEqualTo("diagnostics.rcon-health");
        await Assert.That(ProtocolJson.MessageTypes.ContainsKey("diagnostics.rcon-health")).IsTrue();
    }

    [Test]
    public async Task OperationCompleted_round_trips_an_rcon_health_result()
    {
        Envelope<OperationCompleted> original = Envelope.Create(
            new OperationCompleted(OperationOutcome.Succeeded, Rcon: new RconHealthResult(true, true, null)),
            At,
            serverId: ServerId.New(),
            operationId: OperationId.New());

        Envelope<OperationCompleted> back = ProtocolJson.Deserialize<OperationCompleted>(ProtocolJson.Serialize(original));

        await Assert.That(back.Payload).IsEqualTo(original.Payload);
        await Assert.That(back.Payload.Rcon!.Reachable).IsTrue();
        await Assert.That(back.Payload.Rcon!.Authenticated).IsTrue();
        await Assert.That(back.Payload.Provision).IsNull();
        await Assert.That(back.Payload.Update).IsNull();
    }

    [Test]
    public async Task OperationCompleted_round_trips_a_failed_rcon_result_with_a_detail()
    {
        Envelope<OperationCompleted> original = Envelope.Create(
            new OperationCompleted(
                OperationOutcome.Failed,
                "RCON disabled: empty password",
                Rcon: new RconHealthResult(false, false, "RCON disabled: empty password")),
            At,
            serverId: ServerId.New(),
            operationId: OperationId.New());

        Envelope<OperationCompleted> back = ProtocolJson.Deserialize<OperationCompleted>(ProtocolJson.Serialize(original));

        await Assert.That(back.Payload.Rcon!.Reachable).IsFalse();
        await Assert.That(back.Payload.Rcon!.Detail).IsEqualTo("RCON disabled: empty password");
    }

    [Test]
    public async Task OperationCompleted_without_an_rcon_result_stays_null()
    {
        Envelope<OperationCompleted> original = Envelope.Create(
            new OperationCompleted(OperationOutcome.Succeeded), At, operationId: OperationId.New());

        Envelope<OperationCompleted> back = ProtocolJson.Deserialize<OperationCompleted>(ProtocolJson.Serialize(original));

        await Assert.That(back.Payload.Rcon).IsNull();
    }

    [Test]
    public async Task The_change_is_additive_so_the_protocol_version_stays_one()
    {
        // Read through the supported range's property (not the const directly) so the assertion has a
        // non-constant subject; the point is that adding ProbeRconHealth did not bump the version.
        await Assert.That(ProtocolVersionRange.Supported.Current).IsEqualTo(1);
    }
}
