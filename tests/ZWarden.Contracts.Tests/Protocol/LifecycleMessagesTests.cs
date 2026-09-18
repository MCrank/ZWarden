using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Contracts.Tests.Protocol;

/// <summary>
/// F15: the lifecycle wire surface. <see cref="StartServer"/>/<see cref="StopServer"/>/<see cref="RestartServer"/>
/// are payload-free leaves of the closed <see cref="AgentCommand"/> vocabulary (the target Server rides the
/// envelope, like <see cref="CreateServer"/>), each declaring a unique <c>lifecycle.*</c> discriminator and
/// round-tripping through the one canonical <see cref="ProtocolJson"/>.
/// </summary>
public class LifecycleMessagesTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 13, 8, 30, 0, TimeSpan.Zero);

    private static readonly int[] GracefulLeads = [300, 60, 30, 10];

    private static async Task RoundTripsWithServerOnEnvelope(AgentCommand command)
    {
        ServerId server = ServerId.New();
        Envelope<AgentCommand> original = Envelope.Create(
            command, At, agentId: AgentId.New(), serverId: server, operationId: OperationId.New());

        Envelope<IProtocolMessage> back = ProtocolJson.Deserialize(ProtocolJson.Serialize(original));

        await Assert.That(back.ServerId).IsEqualTo(server);
        await Assert.That(back.Payload).IsEqualTo((IProtocolMessage)command);
    }

    private static async Task DeclaresDiscriminator(AgentCommand command, string discriminator)
    {
        ProtocolMessageAttribute? attribute = (ProtocolMessageAttribute?)Attribute.GetCustomAttribute(
            command.GetType(), typeof(ProtocolMessageAttribute));

        await Assert.That(attribute).IsNotNull();
        await Assert.That(attribute!.Discriminator).IsEqualTo(discriminator);
        await Assert.That(ProtocolJson.MessageTypes.ContainsKey(discriminator)).IsTrue();
        await Assert.That(typeof(AgentCommand).IsAssignableFrom(command.GetType())).IsTrue();
    }

    [Test]
    public async Task StartServer_round_trips_with_the_target_server_on_the_envelope()
        => await RoundTripsWithServerOnEnvelope(new StartServer());

    [Test]
    public async Task StopServer_round_trips_with_the_target_server_on_the_envelope()
        => await RoundTripsWithServerOnEnvelope(new StopServer());

    [Test]
    public async Task RestartServer_round_trips_with_the_target_server_on_the_envelope()
        => await RoundTripsWithServerOnEnvelope(new RestartServer());

    [Test]
    public async Task StartServer_declares_the_lifecycle_discriminator()
        => await DeclaresDiscriminator(new StartServer(), "lifecycle.start-server");

    [Test]
    public async Task StopServer_declares_the_lifecycle_discriminator()
        => await DeclaresDiscriminator(new StopServer(), "lifecycle.stop-server");

    [Test]
    public async Task RestartServer_declares_the_lifecycle_discriminator()
        => await DeclaresDiscriminator(new RestartServer(), "lifecycle.restart-server");

    [Test]
    public async Task RestartServer_carries_its_graceful_plan_across_the_wire()
    {
        // #114: the optional graceful-restart plan is an additive field (ADR 0020) that survives the round-trip.
        AgentCommand command = new RestartServer(new GracefulRestartPlan(GracefulLeads, "Applying mod changes."));
        Envelope<AgentCommand> original = Envelope.Create(
            command, At, agentId: AgentId.New(), serverId: ServerId.New(), operationId: OperationId.New());

        Envelope<IProtocolMessage> back = ProtocolJson.Deserialize(ProtocolJson.Serialize(original));

        RestartServer restored = (RestartServer)back.Payload;
        await Assert.That(restored.Plan).IsNotNull();
        await Assert.That(restored.Plan!.WarningLeadSeconds).IsEquivalentTo(GracefulLeads);
        await Assert.That(restored.Plan!.Reason).IsEqualTo("Applying mod changes.");
    }
}
