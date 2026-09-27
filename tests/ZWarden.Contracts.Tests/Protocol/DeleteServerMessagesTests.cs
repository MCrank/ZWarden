using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Contracts.Tests.Protocol;

/// <summary>
/// #271: <see cref="DeleteServer"/> is a leaf of the closed <see cref="AgentCommand"/> vocabulary — the target Server
/// rides the envelope, and the only payload is the optional graceful-warning plan before the safe stop.
/// </summary>
public class DeleteServerMessagesTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 27, 8, 30, 0, TimeSpan.Zero);

    [Test]
    public async Task DeleteServer_round_trips_a_graceful_plan_with_the_target_server_on_the_envelope()
    {
        ServerId server = ServerId.New();
        Envelope<DeleteServer> original = Envelope.Create(
            new DeleteServer(new GracefulRestartPlan([60, 10], "Retiring this server.")),
            At, agentId: AgentId.New(), serverId: server, operationId: OperationId.New());

        Envelope<DeleteServer> back = ProtocolJson.Deserialize<DeleteServer>(ProtocolJson.Serialize(original));

        await Assert.That(back.ServerId).IsEqualTo(server);
        await Assert.That(back.Payload.Plan!.WarningLeadSeconds).IsEquivalentTo([60, 10]);
        await Assert.That(back.Payload.Plan!.Reason).IsEqualTo("Retiring this server.");
    }

    [Test]
    public async Task DeleteServer_without_a_plan_uses_the_default_warning()
    {
        await Assert.That(new DeleteServer().Plan).IsNull();
    }

    [Test]
    public async Task DeleteServer_declares_the_provisioning_discriminator()
    {
        ProtocolMessageAttribute? attribute = (ProtocolMessageAttribute?)Attribute.GetCustomAttribute(
            typeof(DeleteServer), typeof(ProtocolMessageAttribute));

        await Assert.That(attribute).IsNotNull();
        await Assert.That(attribute!.Discriminator).IsEqualTo("provisioning.delete-server");
    }
}
