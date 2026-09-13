using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Contracts.Tests.Protocol;

/// <summary>
/// F17: the SteamCMD update wire surface. <see cref="UpdateServer"/> is a payload-free leaf of the closed
/// <see cref="AgentCommand"/> vocabulary (the target Server rides the envelope, like <see cref="StartServer"/>),
/// declaring a unique <c>lifecycle.*</c> discriminator; the completion carries an optional
/// <see cref="UpdateResult"/> (the installed build id) that is additive (ADR 0020) and round-trips through the
/// one canonical <see cref="ProtocolJson"/>.
/// </summary>
public class UpdateMessagesTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 13, 8, 30, 0, TimeSpan.Zero);

    [Test]
    public async Task UpdateServer_round_trips_with_the_target_server_on_the_envelope()
    {
        ServerId server = ServerId.New();
        AgentCommand command = new UpdateServer();
        Envelope<AgentCommand> original = Envelope.Create(
            command, At, agentId: AgentId.New(), serverId: server, operationId: OperationId.New());

        Envelope<IProtocolMessage> back = ProtocolJson.Deserialize(ProtocolJson.Serialize(original));

        await Assert.That(back.ServerId).IsEqualTo(server);
        await Assert.That(back.Payload).IsEqualTo((IProtocolMessage)command);
    }

    [Test]
    public async Task UpdateServer_declares_the_lifecycle_discriminator()
    {
        ProtocolMessageAttribute? attribute = (ProtocolMessageAttribute?)Attribute.GetCustomAttribute(
            typeof(UpdateServer), typeof(ProtocolMessageAttribute));

        await Assert.That(attribute).IsNotNull();
        await Assert.That(attribute!.Discriminator).IsEqualTo("lifecycle.update-server");
        await Assert.That(ProtocolJson.MessageTypes.ContainsKey("lifecycle.update-server")).IsTrue();
    }

    [Test]
    public async Task OperationCompleted_round_trips_an_update_result()
    {
        Envelope<OperationCompleted> original = Envelope.Create(
            new OperationCompleted(OperationOutcome.Succeeded, null, null, new UpdateResult("24909836")),
            At,
            serverId: ServerId.New(),
            operationId: OperationId.New());

        Envelope<OperationCompleted> back = ProtocolJson.Deserialize<OperationCompleted>(ProtocolJson.Serialize(original));

        await Assert.That(back.Payload).IsEqualTo(original.Payload);
        await Assert.That(back.Payload.Update!.InstalledBuildId).IsEqualTo("24909836");
        await Assert.That(back.Payload.Provision).IsNull();
    }

    [Test]
    public async Task OperationCompleted_without_an_update_result_stays_null()
    {
        Envelope<OperationCompleted> original = Envelope.Create(
            new OperationCompleted(OperationOutcome.Failed, "steamcmd reported an error"), At, operationId: OperationId.New());

        Envelope<OperationCompleted> back = ProtocolJson.Deserialize<OperationCompleted>(ProtocolJson.Serialize(original));

        await Assert.That(back.Payload.Update).IsNull();
    }
}
