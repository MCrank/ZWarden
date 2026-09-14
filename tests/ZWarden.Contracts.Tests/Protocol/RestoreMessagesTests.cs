using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Contracts.Tests.Protocol;

/// <summary>
/// F25: the restore wire surface. <see cref="RestoreServer"/> is a leaf of the closed
/// <see cref="AgentCommand"/> vocabulary carrying the archive to restore and the lowercase-hex checksum the
/// Agent re-verifies before it unpacks (ADR 0028/0029); the completion carries an optional
/// <see cref="RestoreResult"/> — the restored archive plus the protective pre-restore backup the Agent took
/// inline — that is additive (ADR 0020) and round-trips through the one canonical <see cref="ProtocolJson"/>.
/// </summary>
public class RestoreMessagesTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task RestoreServer_round_trips_with_the_target_server_on_the_envelope()
    {
        ServerId server = ServerId.New();
        AgentCommand command = new RestoreServer("world-20260914-100000-op.tar.gz", "abc123");
        Envelope<AgentCommand> original = Envelope.Create(
            command, At, agentId: AgentId.New(), serverId: server, operationId: OperationId.New());

        Envelope<IProtocolMessage> back = ProtocolJson.Deserialize(ProtocolJson.Serialize(original));

        await Assert.That(back.ServerId).IsEqualTo(server);
        await Assert.That(back.Payload).IsEqualTo((IProtocolMessage)command);
        await Assert.That(((RestoreServer)back.Payload).ArchiveName).IsEqualTo("world-20260914-100000-op.tar.gz");
        await Assert.That(((RestoreServer)back.Payload).Sha256).IsEqualTo("abc123");
    }

    [Test]
    public async Task RestoreServer_declares_the_lifecycle_discriminator()
    {
        ProtocolMessageAttribute? attribute = (ProtocolMessageAttribute?)Attribute.GetCustomAttribute(
            typeof(RestoreServer), typeof(ProtocolMessageAttribute));

        await Assert.That(attribute).IsNotNull();
        await Assert.That(attribute!.Discriminator).IsEqualTo("lifecycle.restore-server");
        await Assert.That(ProtocolJson.MessageTypes.ContainsKey("lifecycle.restore-server")).IsTrue();
    }

    [Test]
    public async Task OperationCompleted_round_trips_a_restore_result_with_its_protective_backup()
    {
        var protective = new BackupResult("world-20260914-095900-pre.tar.gz", 8192, "def456", At);
        var result = new RestoreResult("world-20260914-100000-op.tar.gz", protective);
        Envelope<OperationCompleted> original = Envelope.Create(
            new OperationCompleted(OperationOutcome.Succeeded, Restore: result),
            At,
            serverId: ServerId.New(),
            operationId: OperationId.New());

        Envelope<OperationCompleted> back = ProtocolJson.Deserialize<OperationCompleted>(ProtocolJson.Serialize(original));

        await Assert.That(back.Payload).IsEqualTo(original.Payload);
        await Assert.That(back.Payload.Restore!.RestoredArchiveName).IsEqualTo("world-20260914-100000-op.tar.gz");
        await Assert.That(back.Payload.Restore!.ProtectiveBackup.ArchiveName).IsEqualTo("world-20260914-095900-pre.tar.gz");
        await Assert.That(back.Payload.Restore!.ProtectiveBackup.Sha256).IsEqualTo("def456");
        await Assert.That(back.Payload.Backup).IsNull();
    }

    [Test]
    public async Task OperationCompleted_without_a_restore_result_stays_null()
    {
        Envelope<OperationCompleted> original = Envelope.Create(
            new OperationCompleted(OperationOutcome.Failed, "restore refused: checksum mismatch"),
            At,
            operationId: OperationId.New());

        Envelope<OperationCompleted> back = ProtocolJson.Deserialize<OperationCompleted>(ProtocolJson.Serialize(original));

        await Assert.That(back.Payload.Restore).IsNull();
    }
}
