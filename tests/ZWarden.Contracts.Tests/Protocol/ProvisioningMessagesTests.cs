using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Contracts.Tests.Protocol;

/// <summary>
/// F14 PR-B: the provisioning wire surface. <see cref="CreateServer"/> is a payload-free leaf of the closed
/// <see cref="AgentCommand"/> vocabulary (the target Server rides the envelope), and the completion carries an
/// optional <see cref="ProvisionResult"/> (allocated ports + container id) that round-trips through the one
/// canonical <see cref="ProtocolJson"/>.
/// </summary>
public class ProvisioningMessagesTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 13, 8, 30, 0, TimeSpan.Zero);

    [Test]
    public async Task CreateServer_round_trips_with_the_target_server_on_the_envelope()
    {
        ServerId server = ServerId.New();
        Envelope<CreateServer> original = Envelope.Create(
            new CreateServer(), At, agentId: AgentId.New(), serverId: server, operationId: OperationId.New());

        Envelope<CreateServer> back = ProtocolJson.Deserialize<CreateServer>(ProtocolJson.Serialize(original));

        await Assert.That(back.ServerId).IsEqualTo(server);
        await Assert.That(back.Payload).IsEqualTo(original.Payload);
    }

    [Test]
    public async Task CreateServer_declares_the_provisioning_discriminator()
    {
        ProtocolMessageAttribute? attribute = (ProtocolMessageAttribute?)Attribute.GetCustomAttribute(
            typeof(CreateServer), typeof(ProtocolMessageAttribute));

        await Assert.That(attribute).IsNotNull();
        await Assert.That(attribute!.Discriminator).IsEqualTo("provisioning.create-server");
    }

    [Test]
    public async Task OperationCompleted_round_trips_a_provision_result()
    {
        Envelope<OperationCompleted> original = Envelope.Create(
            new OperationCompleted(OperationOutcome.Succeeded, null, new ProvisionResult(16265, 16266, "c0ffeecafe")),
            At,
            serverId: ServerId.New(),
            operationId: OperationId.New());

        Envelope<OperationCompleted> back = ProtocolJson.Deserialize<OperationCompleted>(ProtocolJson.Serialize(original));

        await Assert.That(back.Payload).IsEqualTo(original.Payload);
        await Assert.That(back.Payload.Provision!.GamePort).IsEqualTo(16265);
        await Assert.That(back.Payload.Provision!.QueryPort).IsEqualTo(16266);
        await Assert.That(back.Payload.Provision!.ContainerId).IsEqualTo("c0ffeecafe");
    }

    [Test]
    public async Task OperationCompleted_without_a_provision_result_stays_null()
    {
        Envelope<OperationCompleted> original = Envelope.Create(
            new OperationCompleted(OperationOutcome.Succeeded), At, operationId: OperationId.New());

        Envelope<OperationCompleted> back = ProtocolJson.Deserialize<OperationCompleted>(ProtocolJson.Serialize(original));

        await Assert.That(back.Payload.Provision).IsNull();
    }
}
