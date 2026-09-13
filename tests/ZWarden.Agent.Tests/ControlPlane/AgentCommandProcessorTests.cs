using ZWarden.Agent.ControlPlane;
using ZWarden.Agent.Docker;
using ZWarden.Agent.Tests.Docker;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests.ControlPlane;

/// <summary>
/// The <see cref="AgentCommandProcessor"/> — the Agent's handling of a dispatched command. A
/// <c>Diagnostics.Ping</c> (F11) completes successfully; a <c>Diagnostics.DockerHealth</c> (F13) reports the
/// Docker probe result on the same operation; a redelivered command is deduped so the work runs once (PRD 20);
/// a command with no operation id, or one this Agent version does not understand, is ignored.
/// </summary>
public class AgentCommandProcessorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

    private static AgentCommandProcessor Processor(IContainerRuntime? runtime = null) =>
        new(TimeProvider.System, runtime ?? new FakeContainerRuntime());

    private static string Json(AgentCommand command, OperationId? operationId = null)
        => ProtocolJson.Serialize(operationId is { } op
            ? Envelope.Create(command, Now, operationId: op)
            : Envelope.Create(command, Now));

    [Test]
    public async Task A_ping_completes_successfully_on_the_same_operation()
    {
        OperationId operationId = OperationId.New();

        Envelope<OperationCompleted>? reply = await Processor().ProcessAsync(Json(new PingAgent(), operationId), CancellationToken.None);

        await Assert.That(reply).IsNotNull();
        await Assert.That(reply!.OperationId).IsEqualTo(operationId);
        await Assert.That(reply.Payload.Outcome).IsEqualTo(OperationOutcome.Succeeded);
    }

    [Test]
    public async Task A_docker_health_probe_succeeds_when_the_daemon_is_reachable()
    {
        var runtime = new FakeContainerRuntime { Health = new DockerHealth(true, "1.53", null) };
        OperationId operationId = OperationId.New();

        Envelope<OperationCompleted>? reply = await Processor(runtime)
            .ProcessAsync(Json(new ProbeDockerHealth(), operationId), CancellationToken.None);

        await Assert.That(reply).IsNotNull();
        await Assert.That(reply!.OperationId).IsEqualTo(operationId);
        await Assert.That(reply.Payload.Outcome).IsEqualTo(OperationOutcome.Succeeded);
        await Assert.That(runtime.ProbeCount).IsEqualTo(1);
    }

    [Test]
    public async Task A_docker_health_probe_fails_with_a_reason_when_the_daemon_is_unreachable()
    {
        var runtime = new FakeContainerRuntime { Health = new DockerHealth(false, null, "The Docker daemon is not reachable.") };

        Envelope<OperationCompleted>? reply = await Processor(runtime)
            .ProcessAsync(Json(new ProbeDockerHealth(), OperationId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Failed);
        await Assert.That(reply.Payload.FailureReason).IsEqualTo("The Docker daemon is not reachable.");
    }

    [Test]
    public async Task A_redelivered_command_is_deduped()
    {
        AgentCommandProcessor sut = Processor();
        string json = Json(new PingAgent(), OperationId.New());

        Envelope<OperationCompleted>? first = await sut.ProcessAsync(json, CancellationToken.None);
        Envelope<OperationCompleted>? second = await sut.ProcessAsync(json, CancellationToken.None);

        await Assert.That(first).IsNotNull();
        await Assert.That(second).IsNull();
    }

    [Test]
    public async Task A_redelivered_docker_health_probe_runs_once()
    {
        var runtime = new FakeContainerRuntime();
        AgentCommandProcessor sut = Processor(runtime);
        string json = Json(new ProbeDockerHealth(), OperationId.New());

        await sut.ProcessAsync(json, CancellationToken.None);
        Envelope<OperationCompleted>? second = await sut.ProcessAsync(json, CancellationToken.None);

        await Assert.That(second).IsNull();
        await Assert.That(runtime.ProbeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Distinct_operations_are_each_handled()
    {
        AgentCommandProcessor sut = Processor();

        Envelope<OperationCompleted>? a = await sut.ProcessAsync(Json(new PingAgent(), OperationId.New()), CancellationToken.None);
        Envelope<OperationCompleted>? b = await sut.ProcessAsync(Json(new PingAgent(), OperationId.New()), CancellationToken.None);

        await Assert.That(a).IsNotNull();
        await Assert.That(b).IsNotNull();
    }

    [Test]
    public async Task A_command_without_an_operation_id_is_ignored()
    {
        await Assert.That(await Processor().ProcessAsync(Json(new PingAgent()), CancellationToken.None)).IsNull();
    }
}
