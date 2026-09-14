using ZWarden.Application.Configuration;
using ZWarden.Application.Mods;

namespace ZWarden.Infrastructure.Tests.Mods;

/// <summary>
/// F22 slice 1: the pure <see cref="ModListEditor"/> that recomputes a Server's <c>WorkshopItems=</c> / <c>Mods=</c>
/// list values from the observed lists (F21) plus an operator intent, and emits them as F20b
/// <see cref="ConfigApplyEdit"/>s (config-as-truth — a mod change is a list-value config edit). Order-preserving,
/// ordinal, de-duped; a no-op returns <see cref="ModListEditStatus.NoChange"/> so the caller skips a pointless
/// Operation. No filesystem, no config parsing — a pure function of (current list, intent).
/// </summary>
public class ModListEditorTests
{
    // ---- EnableMods (append to Mods=) --------------------------------------------------------------------------

    [Test]
    public async Task Enable_appends_new_mod_ids_after_the_existing_ones_in_order()
    {
        ModListEditResult result = ModListEditor.EnableMods(["A", "B"], ["C", "D"]);

        await Assert.That(result.Status).IsEqualTo(ModListEditStatus.Changed);
        await Assert.That(result.Edits.Count).IsEqualTo(1);
        await Assert.That(result.Edits[0]).IsEqualTo(new ConfigApplyEdit("Mods", ConfigEditKind.Text, "A;B;C;D"));
    }

    [Test]
    public async Task Enable_of_an_already_enabled_mod_is_no_change()
    {
        ModListEditResult result = ModListEditor.EnableMods(["A", "B"], ["A"]);

        await Assert.That(result.Status).IsEqualTo(ModListEditStatus.NoChange);
        await Assert.That(result.Edits).IsEmpty();
    }

    [Test]
    public async Task Enable_dedupes_a_repeated_request_and_keeps_the_first_position()
    {
        ModListEditResult result = ModListEditor.EnableMods(["A"], ["B", "B"]);

        await Assert.That(result.Edits[0]).IsEqualTo(new ConfigApplyEdit("Mods", ConfigEditKind.Text, "A;B"));
    }

    [Test]
    public async Task Enable_with_an_empty_request_is_no_change()
    {
        ModListEditResult result = ModListEditor.EnableMods(["A"], []);

        await Assert.That(result.Status).IsEqualTo(ModListEditStatus.NoChange);
    }

    [Test]
    public async Task Enable_compares_mod_ids_ordinally_case_sensitively()
    {
        // PZ Mod ids are case-sensitive (research §6), so "mod" and "Mod" are distinct.
        ModListEditResult result = ModListEditor.EnableMods(["Mod"], ["mod"]);

        await Assert.That(result.Edits[0]).IsEqualTo(new ConfigApplyEdit("Mods", ConfigEditKind.Text, "Mod;mod"));
    }

    // ---- DisableMods (remove from Mods=) -----------------------------------------------------------------------

    [Test]
    public async Task Disable_removes_the_ids_and_preserves_remaining_order()
    {
        ModListEditResult result = ModListEditor.DisableMods(["A", "B", "C"], ["B"]);

        await Assert.That(result.Edits[0]).IsEqualTo(new ConfigApplyEdit("Mods", ConfigEditKind.Text, "A;C"));
    }

    [Test]
    public async Task Disable_of_the_last_mod_yields_an_empty_value()
    {
        ModListEditResult result = ModListEditor.DisableMods(["A"], ["A"]);

        await Assert.That(result.Status).IsEqualTo(ModListEditStatus.Changed);
        await Assert.That(result.Edits[0]).IsEqualTo(new ConfigApplyEdit("Mods", ConfigEditKind.Text, ""));
    }

    [Test]
    public async Task Disable_of_an_absent_mod_is_no_change()
    {
        ModListEditResult result = ModListEditor.DisableMods(["A", "B"], ["Z"]);

        await Assert.That(result.Status).IsEqualTo(ModListEditStatus.NoChange);
    }

    // ---- ReorderMods (order-only rewrite of Mods=) -------------------------------------------------------------

    [Test]
    public async Task Reorder_sets_the_exact_requested_order()
    {
        ModListEditResult result = ModListEditor.ReorderMods(["A", "B", "C"], ["C", "A", "B"]);

        await Assert.That(result.Edits[0]).IsEqualTo(new ConfigApplyEdit("Mods", ConfigEditKind.Text, "C;A;B"));
    }

    [Test]
    public async Task Reorder_to_the_same_order_is_no_change()
    {
        ModListEditResult result = ModListEditor.ReorderMods(["A", "B", "C"], ["A", "B", "C"]);

        await Assert.That(result.Status).IsEqualTo(ModListEditStatus.NoChange);
    }

    [Test]
    public async Task Reorder_that_is_not_a_permutation_of_the_current_set_is_rejected()
    {
        // A reorder must be order-only: adding or dropping an id here would be a silent enable/disable.
        ModListEditResult result = ModListEditor.ReorderMods(["A", "B"], ["A", "C"]);

        await Assert.That(result.Status).IsEqualTo(ModListEditStatus.InvalidReorder);
        await Assert.That(result.Edits).IsEmpty();
    }

    [Test]
    public async Task Reorder_that_drops_an_id_is_rejected()
    {
        ModListEditResult result = ModListEditor.ReorderMods(["A", "B", "C"], ["A", "B"]);

        await Assert.That(result.Status).IsEqualTo(ModListEditStatus.InvalidReorder);
    }

    // ---- AddWorkshopItem (append to WorkshopItems= only) ------------------------------------------------------

    [Test]
    public async Task Add_workshop_item_appends_to_workshop_items_and_leaves_mods_untouched()
    {
        // The provided Mod ids are unknown until the item downloads (two-step install), so Mods= is not touched.
        ModListEditResult result = ModListEditor.AddWorkshopItem(["100"], "200");

        await Assert.That(result.Status).IsEqualTo(ModListEditStatus.Changed);
        await Assert.That(result.Edits.Count).IsEqualTo(1);
        await Assert.That(result.Edits[0]).IsEqualTo(
            new ConfigApplyEdit("WorkshopItems", ConfigEditKind.Text, "100;200"));
    }

    [Test]
    public async Task Add_of_an_already_referenced_workshop_item_is_no_change()
    {
        ModListEditResult result = ModListEditor.AddWorkshopItem(["100", "200"], "200");

        await Assert.That(result.Status).IsEqualTo(ModListEditStatus.NoChange);
    }

    // ---- RemoveWorkshopItems (drop from WorkshopItems= and exclusively-provided Mods=) ------------------------

    [Test]
    public async Task Remove_drops_the_item_and_the_mods_it_exclusively_provides()
    {
        InstalledWorkshopItem[] installed =
        [
            new("100", [new InstalledMod("A", null)]),
            new("200", [new InstalledMod("B", null)]),
        ];

        ModListEditResult result = ModListEditor.RemoveWorkshopItems(
            configuredWorkshopIds: ["100", "200"],
            enabledModIds: ["A", "B"],
            installedItems: installed,
            workshopIdsToRemove: ["100"]);

        await Assert.That(result.Status).IsEqualTo(ModListEditStatus.Changed);
        await Assert.That(result.Edits.Count).IsEqualTo(2);
        // WorkshopItems= edit is emitted first, then Mods=.
        await Assert.That(result.Edits[0]).IsEqualTo(new ConfigApplyEdit("WorkshopItems", ConfigEditKind.Text, "200"));
        await Assert.That(result.Edits[1]).IsEqualTo(new ConfigApplyEdit("Mods", ConfigEditKind.Text, "B"));
    }

    [Test]
    public async Task Remove_keeps_a_mod_still_provided_by_a_remaining_item()
    {
        // Mod "A" is provided by both 100 and 200 (a duplicate); removing 100 must not disable A.
        InstalledWorkshopItem[] installed =
        [
            new("100", [new InstalledMod("A", null)]),
            new("200", [new InstalledMod("A", null)]),
        ];

        ModListEditResult result = ModListEditor.RemoveWorkshopItems(
            configuredWorkshopIds: ["100", "200"],
            enabledModIds: ["A"],
            installedItems: installed,
            workshopIdsToRemove: ["100"]);

        await Assert.That(result.Edits.Count).IsEqualTo(1);
        await Assert.That(result.Edits[0]).IsEqualTo(new ConfigApplyEdit("WorkshopItems", ConfigEditKind.Text, "200"));
    }

    [Test]
    public async Task Remove_of_a_referenced_but_not_installed_item_only_touches_workshop_items()
    {
        // 100 is referenced but has no folder on disk, so we cannot know which mods it provided; leave Mods= alone.
        InstalledWorkshopItem[] installed = [new("200", [new InstalledMod("A", null)])];

        ModListEditResult result = ModListEditor.RemoveWorkshopItems(
            configuredWorkshopIds: ["100", "200"],
            enabledModIds: ["A"],
            installedItems: installed,
            workshopIdsToRemove: ["100"]);

        await Assert.That(result.Edits.Count).IsEqualTo(1);
        await Assert.That(result.Edits[0]).IsEqualTo(new ConfigApplyEdit("WorkshopItems", ConfigEditKind.Text, "200"));
    }

    [Test]
    public async Task Remove_of_an_unreferenced_workshop_item_is_no_change()
    {
        ModListEditResult result = ModListEditor.RemoveWorkshopItems(
            configuredWorkshopIds: ["100"],
            enabledModIds: [],
            installedItems: [],
            workshopIdsToRemove: ["999"]);

        await Assert.That(result.Status).IsEqualTo(ModListEditStatus.NoChange);
    }
}
