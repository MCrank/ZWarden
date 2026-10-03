using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Web.Agents;

namespace ZWarden.Web.Tests.Agents;

/// <summary>#293: the result line a completed Workshop delete shows — what was deleted, and anything the Agent refused.</summary>
public class WorkshopDeletionTextTests
{
    private static WorkshopContentDeletionResult Result(params WorkshopContentDeletionOutcome[] outcomes) =>
        new([.. outcomes.Select((o, i) => new WorkshopContentDeletion((100 + i).ToString(System.Globalization.CultureInfo.InvariantCulture), o))]);

    [Test]
    public async Task Deleted_and_already_gone_items_both_count_as_deleted()
    {
        await Assert.That(WorkshopDeletionText.Describe(Result(WorkshopContentDeletionOutcome.Deleted)))
            .IsEqualTo("Deleted 1 unused download.");
        await Assert.That(WorkshopDeletionText.Describe(
                Result(WorkshopContentDeletionOutcome.Deleted, WorkshopContentDeletionOutcome.AlreadyAbsent)))
            .IsEqualTo("Deleted 2 unused downloads.");
    }

    [Test]
    public async Task Refused_items_are_named_with_the_reason()
    {
        string text = WorkshopDeletionText.Describe(Result(
            WorkshopContentDeletionOutcome.Deleted,
            WorkshopContentDeletionOutcome.RefusedReferenced,
            WorkshopContentDeletionOutcome.RefusedUnsafe));

        await Assert.That(text).IsEqualTo(
            "Deleted 1 unused download. Kept 101 (still in WorkshopItems=). Kept 102 (its folder is a link).");
    }

    [Test]
    public async Task Nothing_deleted_says_so()
    {
        await Assert.That(WorkshopDeletionText.Describe(Result(WorkshopContentDeletionOutcome.RefusedReferenced)))
            .IsEqualTo("Deleted nothing. Kept 100 (still in WorkshopItems=).");
    }
}
