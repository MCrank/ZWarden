using ZWarden.Application.Mods;
using ZWarden.Application.Workshop;

namespace ZWarden.Infrastructure.Tests.Mods;

/// <summary>
/// #291: what one-click Install offers for a Workshop item, from its description's <c>Mod ID:</c> lines (D2) —
/// none (write <c>WorkshopItems=</c> only, Pick parts after boot), one (enable it), or several (the part picker,
/// all pre-ticked). Driven by the real descriptions captured for #290.
/// </summary>
public class ModInstallPlanTests
{
    [Test]
    public async Task A_single_id_item_enables_its_one_id()
    {
        ModInstallPlan plan = ModInstallPlan.For(Item("2553809727"));

        await Assert.That(plan.Kind).IsEqualTo(ModInstallKind.OneId);
        await Assert.That(Values(plan)).IsEqualTo("KillCount");
    }

    [Test]
    public async Task A_multi_mod_item_asks_the_operator_to_choose_among_every_listed_id()
    {
        ModInstallPlan plan = ModInstallPlan.For(Item("1299328280"));

        await Assert.That(plan.Kind).IsEqualTo(ModInstallKind.Choose);
        await Assert.That(Values(plan)).IsEqualTo(
            "1299328280/ToadTraits|1299328280/ToadTraitsDisablePrepared|1299328280/ToadTraitsDisableSpec|1299328280/ToadTraitsDynamic");
    }

    [Test]
    public async Task A_description_without_ids_installs_the_item_only()
    {
        ModInstallPlan plan = ModInstallPlan.For(new WorkshopItemMetadata("123", Found: true, Description: "No ids here."));

        await Assert.That(plan.Kind).IsEqualTo(ModInstallKind.NoIds);
        await Assert.That(plan.CandidateModIds).IsEmpty();
    }

    [Test]
    public async Task An_item_steam_did_not_resolve_installs_the_item_only()
    {
        ModInstallPlan plan = ModInstallPlan.For(WorkshopItemMetadata.NotFound("123"));

        await Assert.That(plan.Kind).IsEqualTo(ModInstallKind.NoIds);
        await Assert.That(plan.WorkshopId).IsEqualTo("123");
    }

    private static WorkshopItemMetadata Item(string workshopId) =>
        new(workshopId, Found: true, Title: "t", Description: File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "workshop-descriptions", $"{workshopId}.txt")));

    private static string Values(ModInstallPlan plan) => string.Join("|", plan.CandidateModIds.Select(i => i.Value));
}
