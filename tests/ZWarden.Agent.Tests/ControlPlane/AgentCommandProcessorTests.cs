using ZWarden.Agent.ControlPlane;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests.ControlPlane;

/// <summary>
/// F11 PR-B: the <see cref="AgentCommandProcessor"/> — the Agent's handling of a dispatched command. A
/// <c>Diagnostics.Ping</c> completes successfully on the same operation; a redelivered command is deduped so
/// the work runs once (PRD 20); a command with no operation id is ignored.
/// </summary>
public class AgentCommandProcessorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

    private static AgentCommandProcessor Processor() => new(TimeProvider.System);

    private static string PingJson(OperationId operationId)
        => ProtocolJson.Serialize(Envelope.Create<AgentCommand>(new PingAgent(), Now, operationId: operationId));

    [Test]
    public async Task A_ping_completes_successfully_on_the_same_operation()
    {
        OperationId operationId = OperationId.New();

        Envelope<OperationCompleted>? reply = Processor().Process(PingJson(operationId));

        await Assert.That(reply).IsNotNull();
        await Assert.That(reply!.OperationId).IsEqualTo(operationId);
        await Assert.That(reply.Payload.Outcome).IsEqualTo(OperationOutcome.Succeeded);
    }

    [Test]
    public async Task A_redelivered_command_is_deduped()
    {
        AgentCommandProcessor sut = Processor();
        string json = PingJson(OperationId.New());

        Envelope<OperationCompleted>? first = sut.Process(json);
        Envelope<OperationCompleted>? second = sut.Process(json);

        await Assert.That(first).IsNotNull();
        await Assert.That(second).IsNull();
    }

    [Test]
    public async Task Distinct_operations_are_each_handled()
    {
        AgentCommandProcessor sut = Processor();

        Envelope<OperationCompleted>? a = sut.Process(PingJson(OperationId.New()));
        Envelope<OperationCompleted>? b = sut.Process(PingJson(OperationId.New()));

        await Assert.That(a).IsNotNull();
        await Assert.That(b).IsNotNull();
    }

    [Test]
    public async Task A_command_without_an_operation_id_is_ignored()
    {
        string json = ProtocolJson.Serialize(Envelope.Create<AgentCommand>(new PingAgent(), Now));

        await Assert.That(Processor().Process(json)).IsNull();
    }
}
