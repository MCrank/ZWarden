using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Contracts.Tests.Protocol;

/// <summary>
/// F24: the backup wire surface. <see cref="BackupServer"/> is a payload-free leaf of the closed
/// <see cref="AgentCommand"/> vocabulary (the target Server rides the envelope, like <see cref="UpdateServer"/>),
/// declaring a unique <c>lifecycle.*</c> discriminator; the completion carries an optional
/// <see cref="BackupResult"/> (the archive locator, size, and checksum) that is additive (ADR 0020) and
/// round-trips through the one canonical <see cref="ProtocolJson"/>.
/// </summary>
public class BackupMessagesTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task BackupServer_round_trips_with_the_target_server_on_the_envelope()
    {
        ServerId server = ServerId.New();
        AgentCommand command = new BackupServer();
        Envelope<AgentCommand> original = Envelope.Create(
            command, At, agentId: AgentId.New(), serverId: server, operationId: OperationId.New());

        Envelope<IProtocolMessage> back = ProtocolJson.Deserialize(ProtocolJson.Serialize(original));

        await Assert.That(back.ServerId).IsEqualTo(server);
        await Assert.That(back.Payload).IsEqualTo((IProtocolMessage)command);
    }

    [Test]
    public async Task BackupServer_declares_the_lifecycle_discriminator()
    {
        ProtocolMessageAttribute? attribute = (ProtocolMessageAttribute?)Attribute.GetCustomAttribute(
            typeof(BackupServer), typeof(ProtocolMessageAttribute));

        await Assert.That(attribute).IsNotNull();
        await Assert.That(attribute!.Discriminator).IsEqualTo("lifecycle.backup-server");
        await Assert.That(ProtocolJson.MessageTypes.ContainsKey("lifecycle.backup-server")).IsTrue();
    }

    [Test]
    public async Task OperationCompleted_round_trips_a_backup_result()
    {
        var result = new BackupResult("world-20260914-100000.tar.gz", 4096, "abc123", At);
        Envelope<OperationCompleted> original = Envelope.Create(
            new OperationCompleted(OperationOutcome.Succeeded, Backup: result),
            At,
            serverId: ServerId.New(),
            operationId: OperationId.New());

        Envelope<OperationCompleted> back = ProtocolJson.Deserialize<OperationCompleted>(ProtocolJson.Serialize(original));

        await Assert.That(back.Payload).IsEqualTo(original.Payload);
        await Assert.That(back.Payload.Backup!.ArchiveName).IsEqualTo("world-20260914-100000.tar.gz");
        await Assert.That(back.Payload.Backup!.SizeBytes).IsEqualTo(4096L);
        await Assert.That(back.Payload.Backup!.Sha256).IsEqualTo("abc123");
        await Assert.That(back.Payload.Update).IsNull();
    }

    [Test]
    public async Task OperationCompleted_without_a_backup_result_stays_null()
    {
        Envelope<OperationCompleted> original = Envelope.Create(
            new OperationCompleted(OperationOutcome.Failed, "backup failed: disk full"), At, operationId: OperationId.New());

        Envelope<OperationCompleted> back = ProtocolJson.Deserialize<OperationCompleted>(ProtocolJson.Serialize(original));

        await Assert.That(back.Payload.Backup).IsNull();
    }

    [Test]
    public async Task DeleteBackup_round_trips_its_archive_name()
    {
        ServerId server = ServerId.New();
        AgentCommand command = new DeleteBackup("world-20260914-100000.tar.gz");
        Envelope<AgentCommand> original = Envelope.Create(
            command, At, agentId: AgentId.New(), serverId: server, operationId: OperationId.New());

        Envelope<IProtocolMessage> back = ProtocolJson.Deserialize(ProtocolJson.Serialize(original));

        await Assert.That(back.ServerId).IsEqualTo(server);
        await Assert.That(((DeleteBackup)back.Payload).ArchiveName).IsEqualTo("world-20260914-100000.tar.gz");
        await Assert.That(ProtocolJson.MessageTypes.ContainsKey("lifecycle.delete-backup")).IsTrue();
    }

    [Test]
    public async Task OperationCompleted_round_trips_a_backup_deletion_result()
    {
        Envelope<OperationCompleted> original = Envelope.Create(
            new OperationCompleted(OperationOutcome.Succeeded, BackupDeletion: new BackupDeletionResult("world-1.tar.gz")),
            At,
            serverId: ServerId.New(),
            operationId: OperationId.New());

        Envelope<OperationCompleted> back = ProtocolJson.Deserialize<OperationCompleted>(ProtocolJson.Serialize(original));

        await Assert.That(back.Payload.BackupDeletion!.ArchiveName).IsEqualTo("world-1.tar.gz");
        await Assert.That(back.Payload.Backup).IsNull();
    }
}
