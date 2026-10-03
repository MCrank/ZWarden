using ZWarden.Application.Mods;
using ZWarden.Domain.Ids;

namespace ZWarden.Infrastructure.Tests.Mods;

/// <summary>
/// #292 live pass: which installed mods declare each other incompatible (<c>incompatible=</c> in <c>mod.info</c>), so
/// the parts picker can stop two builds of one mod (Equipment UI's <c>EQUIPMENT_UI</c> / <c>EQUIPMENT_UI_B42</c>) being
/// turned on together. A declaration counts both ways; ids are compared ordinally.
/// </summary>
public class ModPartConflictsTests
{
    [Test]
    public async Task A_declared_incompatibility_counts_both_ways()
    {
        ModInventory inventory = Inventory(new InstalledWorkshopItem("100",
        [
            new InstalledMod("EQUIPMENT_UI", null, Incompatible: ["EQUIPMENT_UI_B42"]),
            new InstalledMod("EQUIPMENT_UI_B42", null),
            new InstalledMod("equipmentuipatch", null, Incompatible: ["EQUIPMENT_UI_B42"]),
        ]));

        ModPartConflicts conflicts = ModPartConflicts.From(inventory);

        await Assert.That(conflicts.Of("EQUIPMENT_UI")).IsEquivalentTo(["EQUIPMENT_UI_B42"]);
        await Assert.That(conflicts.Of("EQUIPMENT_UI_B42")).IsEquivalentTo(["EQUIPMENT_UI", "equipmentuipatch"]);
        await Assert.That(conflicts.Of("equipmentuipatch")).IsEquivalentTo(["EQUIPMENT_UI_B42"]);
    }

    [Test]
    public async Task Conflicts_with_a_chosen_set_names_only_the_chosen_parts_it_clashes_with()
    {
        ModPartConflicts conflicts = ModPartConflicts.From(Inventory(new InstalledWorkshopItem("100",
        [
            new InstalledMod("A", null, Incompatible: ["B", "C"]),
            new InstalledMod("B", null),
            new InstalledMod("C", null),
        ])));

        await Assert.That(conflicts.With("A", ["B", "A"])).IsEquivalentTo(["B"]);
        await Assert.That(conflicts.With("C", ["B"])).IsEmpty();
        await Assert.That(conflicts.AnyAmong(["B", "C"])).IsFalse();
        await Assert.That(conflicts.AnyAmong(["A", "C"])).IsTrue();
    }

    [Test]
    public async Task No_inventory_means_no_known_conflicts()
    {
        ModPartConflicts conflicts = ModPartConflicts.From(null);

        await Assert.That(conflicts.Of("A")).IsEmpty();
        await Assert.That(conflicts.AnyAmong(["A", "B"])).IsFalse();
    }

    private static ModInventory Inventory(params InstalledWorkshopItem[] items) =>
        new(ServerId.New(), AgentId.New(), items, [], [], [], DateTimeOffset.UtcNow);
}
