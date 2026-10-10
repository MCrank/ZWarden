using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;

namespace ZWarden.Contracts.Tests.Protocol;

/// <summary>
/// #379: the automatic-backup wire surface. The four world-changing commands carry an additive <c>BackupFirst</c>
/// flag (ADR 0020, default <c>false</c>, so an older control plane's command still means "no backup"), and the
/// completion carries the backup the Agent took first in an additive <see cref="OperationCompleted.PreOperationBackup"/>.
/// </summary>
public class PreOperationBackupMessagesTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 10, 10, 0, 0, TimeSpan.Zero);

    private static TCommand RoundTrip<TCommand>(TCommand command)
        where TCommand : AgentCommand
    {
        Envelope<AgentCommand> original = Envelope.Create<AgentCommand>(
            command, At, agentId: AgentId.New(), serverId: ServerId.New(), operationId: OperationId.New());
        return (TCommand)ProtocolJson.Deserialize(ProtocolJson.Serialize(original)).Payload;
    }

    [Test]
    public async Task BackupFirst_round_trips_on_each_world_changing_command()
    {
        await Assert.That(RoundTrip(new ConfigApply(PzConfigFile.Ini, "b", [], BackupFirst: true)).BackupFirst).IsTrue();
        await Assert.That(RoundTrip(new ConfigApplyRaw(PzConfigFile.Ini, "b", "corr", BackupFirst: true)).BackupFirst).IsTrue();
        await Assert.That(RoundTrip(new UpdateServer(BackupFirst: true)).BackupFirst).IsTrue();
        await Assert.That(RoundTrip(new RestartServer(BackupFirst: true)).BackupFirst).IsTrue();
    }

    [Test]
    public async Task BackupFirst_defaults_to_false_when_absent_from_the_wire()
    {
        await Assert.That(RoundTrip(new ConfigApply(PzConfigFile.Ini, "b", [])).BackupFirst).IsFalse();
        await Assert.That(RoundTrip(new ConfigApplyRaw(PzConfigFile.Ini, "b", "corr")).BackupFirst).IsFalse();
        await Assert.That(RoundTrip(new UpdateServer()).BackupFirst).IsFalse();
        await Assert.That(RoundTrip(new RestartServer()).BackupFirst).IsFalse();
    }

    [Test]
    public async Task OperationCompleted_round_trips_the_pre_operation_backup_on_a_failed_command_too()
    {
        var backup = new BackupResult("world-20261010-100000-op-pre-op.tar.gz", 4096, "abc123", At, "not saved first");
        Envelope<OperationCompleted> original = Envelope.Create(
            new OperationCompleted(OperationOutcome.Failed, "drift", PreOperationBackup: backup),
            At,
            serverId: ServerId.New(),
            operationId: OperationId.New());

        Envelope<OperationCompleted> back = ProtocolJson.Deserialize<OperationCompleted>(ProtocolJson.Serialize(original));

        await Assert.That(back.Payload.PreOperationBackup).IsEqualTo(backup);
        await Assert.That(back.Payload.Backup).IsNull();
    }

    [Test]
    public async Task OperationCompleted_without_a_pre_operation_backup_stays_null()
    {
        Envelope<OperationCompleted> back = ProtocolJson.Deserialize<OperationCompleted>(ProtocolJson.Serialize(
            Envelope.Create(new OperationCompleted(OperationOutcome.Succeeded), At, operationId: OperationId.New())));

        await Assert.That(back.Payload.PreOperationBackup).IsNull();
    }
}
