using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Contracts.Tests.Protocol;

/// <summary>
/// #293: <see cref="DeleteWorkshopContent"/> is a leaf of the closed <see cref="AgentCommand"/> vocabulary — the target
/// Server rides the envelope, the payload is the Workshop ids to delete — and its completion reports each id's outcome.
/// </summary>
public class DeleteWorkshopContentMessagesTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 3, 18, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task DeleteWorkshopContent_round_trips_its_ids_with_the_target_server_on_the_envelope()
    {
        ServerId server = ServerId.New();
        Envelope<DeleteWorkshopContent> original = Envelope.Create(
            new DeleteWorkshopContent(["2392709985", "2553809727"]),
            At, agentId: AgentId.New(), serverId: server, operationId: OperationId.New());

        Envelope<DeleteWorkshopContent> back =
            ProtocolJson.Deserialize<DeleteWorkshopContent>(ProtocolJson.Serialize(original));

        await Assert.That(back.ServerId).IsEqualTo(server);
        await Assert.That(back.Payload.WorkshopIds).IsEquivalentTo(["2392709985", "2553809727"]);
    }

    [Test]
    public async Task DeleteWorkshopContent_declares_the_mods_discriminator()
    {
        ProtocolMessageAttribute? attribute = (ProtocolMessageAttribute?)Attribute.GetCustomAttribute(
            typeof(DeleteWorkshopContent), typeof(ProtocolMessageAttribute));

        await Assert.That(attribute).IsNotNull();
        await Assert.That(attribute!.Discriminator).IsEqualTo("mods.delete-workshop-content");
    }

    [Test]
    public async Task A_completion_round_trips_each_ids_outcome()
    {
        Envelope<OperationCompleted> original = Envelope.Create(
            new OperationCompleted(
                OperationOutcome.Succeeded,
                WorkshopContentDeletion: new WorkshopContentDeletionResult(
                [
                    new WorkshopContentDeletion("2392709985", WorkshopContentDeletionOutcome.Deleted),
                    new WorkshopContentDeletion("111", WorkshopContentDeletionOutcome.AlreadyAbsent),
                    new WorkshopContentDeletion("2553809727", WorkshopContentDeletionOutcome.RefusedReferenced),
                    new WorkshopContentDeletion("222", WorkshopContentDeletionOutcome.RefusedUnsafe),
                ])),
            At, serverId: ServerId.New(), operationId: OperationId.New());

        Envelope<OperationCompleted> back = ProtocolJson.Deserialize<OperationCompleted>(ProtocolJson.Serialize(original));

        IReadOnlyList<WorkshopContentDeletion> items = back.Payload.WorkshopContentDeletion!.Items;
        await Assert.That(items.Select(i => i.WorkshopId)).IsEquivalentTo(["2392709985", "111", "2553809727", "222"]);
        await Assert.That(items.Select(i => i.Outcome)).IsEquivalentTo(
        [
            WorkshopContentDeletionOutcome.Deleted,
            WorkshopContentDeletionOutcome.AlreadyAbsent,
            WorkshopContentDeletionOutcome.RefusedReferenced,
            WorkshopContentDeletionOutcome.RefusedUnsafe,
        ]);
        await Assert.That(back.Payload.BackupDeletion).IsNull();
    }

    [Test]
    public async Task The_outcome_travels_as_its_name()
    {
        string json = ProtocolJson.Serialize(Envelope.Create(
            new OperationCompleted(
                OperationOutcome.Succeeded,
                WorkshopContentDeletion: new WorkshopContentDeletionResult(
                    [new WorkshopContentDeletion("1", WorkshopContentDeletionOutcome.RefusedReferenced)])),
            At, operationId: OperationId.New()));

        await Assert.That(json).Contains("\"RefusedReferenced\"");
    }
}
