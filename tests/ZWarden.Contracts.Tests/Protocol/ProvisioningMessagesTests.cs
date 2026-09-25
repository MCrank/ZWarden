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
    public async Task CreateServer_without_a_requested_port_defaults_to_the_agents_stride()
    {
        await Assert.That(new CreateServer().GamePort).IsNull();
    }

    [Test]
    public async Task CreateServer_round_trips_an_operator_chosen_game_port()
    {
        Envelope<CreateServer> original = Envelope.Create(
            new CreateServer(GamePort: 27015), At, agentId: AgentId.New(), serverId: ServerId.New(), operationId: OperationId.New());

        Envelope<CreateServer> back = ProtocolJson.Deserialize<CreateServer>(ProtocolJson.Serialize(original));

        await Assert.That(back.Payload.GamePort).IsEqualTo(27015);
    }

    [Test]
    public async Task RecreateServer_round_trips_a_new_game_port_and_a_graceful_plan()
    {
        ServerId server = ServerId.New();
        Envelope<RecreateServer> original = Envelope.Create(
            new RecreateServer(27015, new GracefulRestartPlan([60, 10], "Changing ports.")),
            At, agentId: AgentId.New(), serverId: server, operationId: OperationId.New());

        Envelope<RecreateServer> back = ProtocolJson.Deserialize<RecreateServer>(ProtocolJson.Serialize(original));

        await Assert.That(back.ServerId).IsEqualTo(server);
        await Assert.That(back.Payload.GamePort).IsEqualTo(27015);
        await Assert.That(back.Payload.Plan!.WarningLeadSeconds).IsEquivalentTo([60, 10]);
        await Assert.That(back.Payload.Plan!.Reason).IsEqualTo("Changing ports.");
    }

    [Test]
    public async Task RecreateServer_without_arguments_keeps_the_ports_and_the_default_warning()
    {
        RecreateServer command = new();

        await Assert.That(command.GamePort).IsNull();
        await Assert.That(command.Plan).IsNull();
    }

    [Test]
    public async Task RecreateServer_declares_the_provisioning_discriminator()
    {
        ProtocolMessageAttribute? attribute = (ProtocolMessageAttribute?)Attribute.GetCustomAttribute(
            typeof(RecreateServer), typeof(ProtocolMessageAttribute));

        await Assert.That(attribute).IsNotNull();
        await Assert.That(attribute!.Discriminator).IsEqualTo("provisioning.recreate-server");
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
