using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Contracts.Tests.Protocol;

/// <summary>
/// F28: the remote-console wire surface. <see cref="ExecuteConsoleCommand"/> is a leaf of the closed
/// <see cref="AgentCommand"/> vocabulary carrying a <c>console.execute</c> discriminator and one operator-authored
/// RCON line — named <c>Input</c>, deliberately <b>not</b> <c>Command</c>/<c>Cmd</c>/<c>Exec</c>/… so the
/// closed-vocabulary canary (trust-boundaries.md §9 rule 3) stays green: it is a policy-gated RCON line, not a
/// free-form shell string. The completion carries an optional <see cref="ConsoleCommandResult"/> whose
/// <c>Output</c> is untrusted PZ text; the addition is additive (ADR 0020), so
/// <see cref="ProtocolVersion.Current"/> stays 1.
/// </summary>
public class ConsoleMessagesTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 15, 9, 0, 0, TimeSpan.Zero);

    private static Envelope<IProtocolMessage> RoundTrip(AgentCommand command, ServerId server) =>
        ProtocolJson.Deserialize(ProtocolJson.Serialize(
            Envelope.Create(command, At, agentId: AgentId.New(), serverId: server, operationId: OperationId.New())));

    [Test]
    public async Task ExecuteConsoleCommand_round_trips_its_input_with_the_target_server_on_the_envelope()
    {
        ServerId server = ServerId.New();

        Envelope<IProtocolMessage> back = RoundTrip(new ExecuteConsoleCommand("servermsg \"hello\""), server);

        await Assert.That(back.ServerId).IsEqualTo(server);
        await Assert.That(back.Payload).IsTypeOf<ExecuteConsoleCommand>();
        await Assert.That(((ExecuteConsoleCommand)back.Payload).Input).IsEqualTo("servermsg \"hello\"");
    }

    [Test]
    public async Task ExecuteConsoleCommand_declares_its_registered_discriminator()
    {
        ProtocolMessageAttribute? attribute = (ProtocolMessageAttribute?)Attribute.GetCustomAttribute(
            typeof(ExecuteConsoleCommand), typeof(ProtocolMessageAttribute));

        await Assert.That(attribute).IsNotNull();
        await Assert.That(attribute!.Discriminator).IsEqualTo("console.execute");
        await Assert.That(ProtocolJson.MessageTypes.ContainsKey("console.execute")).IsTrue();
    }

    [Test]
    public async Task OperationCompleted_round_trips_a_console_command_result()
    {
        Envelope<OperationCompleted> original = Envelope.Create(
            new OperationCompleted(
                OperationOutcome.Succeeded,
                ConsoleCommand: new ConsoleCommandResult("Players connected (0): ", Truncated: false)),
            At,
            serverId: ServerId.New(),
            operationId: OperationId.New());

        Envelope<OperationCompleted> back = ProtocolJson.Deserialize<OperationCompleted>(ProtocolJson.Serialize(original));

        await Assert.That(back.Payload.ConsoleCommand!.Output).IsEqualTo("Players connected (0): ");
        await Assert.That(back.Payload.ConsoleCommand!.Truncated).IsFalse();
        await Assert.That(back.Payload.PlayerAction).IsNull();
    }

    [Test]
    public async Task OperationCompleted_round_trips_a_truncated_console_result()
    {
        Envelope<OperationCompleted> original = Envelope.Create(
            new OperationCompleted(
                OperationOutcome.Succeeded,
                ConsoleCommand: new ConsoleCommandResult("...huge output...", Truncated: true)),
            At,
            operationId: OperationId.New());

        Envelope<OperationCompleted> back = ProtocolJson.Deserialize<OperationCompleted>(ProtocolJson.Serialize(original));

        await Assert.That(back.Payload.ConsoleCommand!.Truncated).IsTrue();
    }

    [Test]
    public async Task OperationCompleted_without_a_console_result_stays_null()
    {
        Envelope<OperationCompleted> original = Envelope.Create(
            new OperationCompleted(OperationOutcome.Succeeded), At, operationId: OperationId.New());

        Envelope<OperationCompleted> back = ProtocolJson.Deserialize<OperationCompleted>(ProtocolJson.Serialize(original));

        await Assert.That(back.Payload.ConsoleCommand).IsNull();
    }

    [Test]
    public async Task The_change_is_additive_so_the_protocol_version_stays_one()
    {
        await Assert.That(ProtocolVersionRange.Supported.Current).IsEqualTo(1);
    }
}
