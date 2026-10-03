using ZWarden.Application.Mods;
using ZWarden.Domain.Ids;

namespace ZWarden.Infrastructure.Tests.Mods;

/// <summary>
/// #292 D2: the F21 compatibility findings fold into the Mods table as a one-line warning under the row they concern,
/// derived from the same <c>mod.info</c> metadata the inventory carries: an enabled part needs a mod that isn't on
/// (installed or not), conflicts with another enabled mod, or shares its id with another downloaded item.
/// </summary>
public class ModRowWarningsTests
{
    private static readonly ServerId Server = ServerId.New();

    [Test]
    public async Task A_part_requiring_a_mod_that_is_not_installed_warns_on_its_row()
    {
        ModInventory inventory = Inventory(
            [Installed("100", new InstalledMod("A", "A", Requires: ["Lib"]))], enabled: ["A"]);

        IReadOnlyDictionary<string, IReadOnlyList<string>> warnings = ModRowWarnings.For(Table(["A"], ("100", ["A"])), inventory);

        await Assert.That(Text(warnings, "item:100")).IsEqualTo("A needs Lib, which isn't installed.");
    }

    [Test]
    public async Task A_part_requiring_an_installed_mod_that_is_not_on_says_to_turn_it_on()
    {
        ModInventory inventory = Inventory(
            [Installed("100", new InstalledMod("A", null, Requires: ["Lib"])), Installed("200", new InstalledMod("Lib", null))],
            enabled: ["A"]);

        IReadOnlyDictionary<string, IReadOnlyList<string>> warnings = ModRowWarnings.For(Table(["A"], ("100", ["A"])), inventory);

        await Assert.That(Text(warnings, "item:100")).IsEqualTo("A needs Lib, which is downloaded but not turned on.");
    }

    [Test]
    public async Task A_part_declared_incompatible_with_another_enabled_mod_warns()
    {
        ModInventory inventory = Inventory(
            [Installed("100", new InstalledMod("A", null, Incompatible: ["B"])), Installed("200", new InstalledMod("B", null))],
            enabled: ["A", "B"]);

        IReadOnlyDictionary<string, IReadOnlyList<string>> warnings =
            ModRowWarnings.For(Table(["A", "B"], ("100", ["A"]), ("200", ["B"])), inventory);

        await Assert.That(Text(warnings, "item:100")).IsEqualTo("A conflicts with B, which is also on.");
        await Assert.That(warnings.ContainsKey("item:200")).IsFalse();
    }

    [Test]
    public async Task An_enabled_id_two_downloaded_items_provide_warns()
    {
        ModInventory inventory = Inventory(
            [Installed("100", new InstalledMod("A", null)), Installed("200", new InstalledMod("A", null))], enabled: ["A"]);

        IReadOnlyDictionary<string, IReadOnlyList<string>> warnings = ModRowWarnings.For(Table(["A"], ("100", ["A"])), inventory);

        await Assert.That(Text(warnings, "item:100")).IsEqualTo("A is also provided by another download.");
    }

    [Test]
    public async Task A_satisfied_row_and_a_missing_inventory_have_no_warnings()
    {
        ModTableView table = Table(["A", "Lib"], ("100", ["A", "Lib"]));
        ModInventory inventory = Inventory(
            [Installed("100", new InstalledMod("A", null, Requires: ["Lib"]), new InstalledMod("Lib", null))], enabled: ["A", "Lib"]);

        await Assert.That(ModRowWarnings.For(table, inventory)).IsEmpty();
        await Assert.That(ModRowWarnings.For(table, null)).IsEmpty();
    }

    private static ModTableView Table(IReadOnlyList<string> enabled, params (string WorkshopId, string[] Observed)[] items) =>
        ModTable.Build(new ServerModOverview(
            Server,
            true,
            null,
            [.. items.Select(i => new ModItemView(i.WorkshopId, ModChangeStatus.Active, null, null, null, null, [], [], i.Observed, true))],
            [.. enabled.Select(m => new ModIdView(m, ModChangeStatus.Active))]));

    private static ModInventory Inventory(IReadOnlyList<InstalledWorkshopItem> installed, IReadOnlyList<string> enabled) =>
        new(Server, AgentId.New(), installed, [.. installed.Select(i => i.WorkshopId)], enabled, [], DateTimeOffset.UtcNow);

    private static InstalledWorkshopItem Installed(string workshopId, params InstalledMod[] mods) => new(workshopId, mods);

    private static string Text(IReadOnlyDictionary<string, IReadOnlyList<string>> warnings, string key) =>
        string.Join(" / ", warnings[key]);
}
