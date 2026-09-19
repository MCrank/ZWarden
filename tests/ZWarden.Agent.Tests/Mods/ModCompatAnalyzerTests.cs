using ZWarden.Agent.Mods;
using ZWarden.Contracts.Protocol.Messages;

namespace ZWarden.Agent.Tests.Mods;

/// <summary>
/// F21: the pure compatibility analyzer. It reconciles three lists — the mods installed on disk (with the Workshop
/// item each came from), the <c>WorkshopItems=</c> the config references, and the <c>Mods=</c> it enables — into the
/// four findings, with no filesystem. The whole truth table is exercised here.
/// </summary>
public class ModCompatAnalyzerTests
{
    private static DiscoveredWorkshopItem Item(string workshopId, params string[] modIds) =>
        new(workshopId, [.. modIds.Select(m => new DiscoveredMod(m, null))]);

    private static IReadOnlyList<ModCompatFinding> Analyze(
        IReadOnlyList<DiscoveredWorkshopItem> installed,
        IReadOnlyList<string> configured,
        IReadOnlyList<string> enabled) =>
        ModCompatAnalyzer.Analyze(installed, configured, enabled);

    [Test]
    public async Task A_fully_reconciled_server_has_no_findings()
    {
        IReadOnlyList<ModCompatFinding> findings = Analyze(
            [Item("111", "ModA")],
            configured: ["111"],
            enabled: ["ModA"]);

        await Assert.That(findings).IsEmpty();
    }

    [Test]
    public async Task A_referenced_workshop_id_absent_on_disk_is_reported()
    {
        IReadOnlyList<ModCompatFinding> findings = Analyze(
            [Item("111", "ModA")],
            configured: ["111", "999"],
            enabled: ["ModA"]);

        await Assert.That(findings.Count).IsEqualTo(1);
        await Assert.That(findings[0].Kind).IsEqualTo(ModCompatKind.ReferencedNotInstalled);
        await Assert.That(findings[0].Subject).IsEqualTo("999");
    }

    [Test]
    public async Task An_enabled_mod_no_installed_item_provides_is_reported()
    {
        IReadOnlyList<ModCompatFinding> findings = Analyze(
            [Item("111", "ModA")],
            configured: ["111"],
            enabled: ["ModA", "Ghost"]);

        ModCompatFinding finding = findings.Single(f => f.Kind == ModCompatKind.EnabledButMissing);
        await Assert.That(finding.Subject).IsEqualTo("Ghost");
    }

    [Test]
    public async Task An_installed_mod_absent_from_the_enabled_list_is_reported_as_inactive()
    {
        IReadOnlyList<ModCompatFinding> findings = Analyze(
            [Item("111", "ModA", "ModB")],
            configured: ["111"],
            enabled: ["ModA"]);

        ModCompatFinding finding = findings.Single(f => f.Kind == ModCompatKind.InstalledButInactive);
        await Assert.That(finding.Subject).IsEqualTo("ModB");
    }

    [Test]
    public async Task A_mod_id_two_installed_items_both_provide_is_a_duplicate()
    {
        IReadOnlyList<ModCompatFinding> findings = Analyze(
            [Item("111", "Shared"), Item("222", "Shared")],
            configured: ["111", "222"],
            enabled: ["Shared"]);

        ModCompatFinding finding = findings.Single(f => f.Kind == ModCompatKind.DuplicateModId);
        await Assert.That(finding.Subject).IsEqualTo("Shared");
    }

    [Test]
    public async Task A_duplicate_id_is_reported_once_not_per_provider()
    {
        IReadOnlyList<ModCompatFinding> findings = Analyze(
            [Item("111", "Shared"), Item("222", "Shared"), Item("333", "Shared")],
            configured: [],
            enabled: ["Shared"]);

        await Assert.That(findings.Count(f => f.Kind == ModCompatKind.DuplicateModId)).IsEqualTo(1);
    }

    [Test]
    public async Task Mod_id_comparison_is_case_sensitive()
    {
        // PZ Mod ids are case-sensitive tokens; an enabled "moda" is genuinely not the installed "ModA".
        IReadOnlyList<ModCompatFinding> findings = Analyze(
            [Item("111", "ModA")],
            configured: ["111"],
            enabled: ["moda"]);

        await Assert.That(findings.Any(f => f.Kind == ModCompatKind.EnabledButMissing && f.Subject == "moda")).IsTrue();
        await Assert.That(findings.Any(f => f.Kind == ModCompatKind.InstalledButInactive && f.Subject == "ModA")).IsTrue();
    }

    [Test]
    public async Task Findings_are_grouped_by_kind_in_enum_order()
    {
        IReadOnlyList<ModCompatFinding> findings = Analyze(
            [Item("111", "ModA", "Inactive"), Item("222", "ModA")],
            configured: ["111", "222", "999"],
            enabled: ["ModA", "Ghost"]);

        // One of each kind, emitted grouped in enum order: referenced-not-installed(999),
        // enabled-missing(Ghost), installed-inactive(Inactive), duplicate(ModA).
        await Assert.That(findings.Count).IsEqualTo(4);
        await Assert.That(findings[0].Kind).IsEqualTo(ModCompatKind.ReferencedNotInstalled);
        await Assert.That(findings[1].Kind).IsEqualTo(ModCompatKind.EnabledButMissing);
        await Assert.That(findings[2].Kind).IsEqualTo(ModCompatKind.InstalledButInactive);
        await Assert.That(findings[3].Kind).IsEqualTo(ModCompatKind.DuplicateModId);
    }

    // #110 — dependency and incompatibility findings, computed from mod.info require=/incompatible= carried on the
    // installed mods. Both fire only for mods that are actually enabled (loading), so an inactive mod's declarations
    // never raise noise.
    private static DiscoveredWorkshopItem ItemWith(string workshopId, params DiscoveredMod[] mods) => new(workshopId, mods);

    private static DiscoveredMod Mod(string id, IReadOnlyList<string>? requires = null, IReadOnlyList<string>? incompatible = null) =>
        new(id, null, Requires: requires, Incompatible: incompatible);

    [Test]
    public async Task An_enabled_mods_missing_dependency_is_reported()
    {
        IReadOnlyList<ModCompatFinding> findings = Analyze(
            [ItemWith("111", Mod("ModA", requires: ["DepX"]))],
            configured: ["111"],
            enabled: ["ModA"]);

        await Assert.That(findings.Any(f => f.Kind == ModCompatKind.RequiresMissing && f.Subject == "DepX")).IsTrue();
    }

    [Test]
    public async Task A_satisfied_dependency_produces_no_finding()
    {
        IReadOnlyList<ModCompatFinding> findings = Analyze(
            [ItemWith("111", Mod("ModA", requires: ["DepX"])), ItemWith("222", Mod("DepX"))],
            configured: ["111", "222"],
            enabled: ["ModA", "DepX"]);

        await Assert.That(findings.Any(f => f.Kind == ModCompatKind.RequiresMissing)).IsFalse();
    }

    [Test]
    public async Task A_dependency_of_a_mod_that_is_not_enabled_is_not_reported()
    {
        IReadOnlyList<ModCompatFinding> findings = Analyze(
            [ItemWith("111", Mod("ModA", requires: ["DepX"]))],
            configured: ["111"],
            enabled: []);

        await Assert.That(findings.Any(f => f.Kind == ModCompatKind.RequiresMissing)).IsFalse();
    }

    [Test]
    public async Task Two_enabled_mutually_present_incompatible_mods_are_reported()
    {
        IReadOnlyList<ModCompatFinding> findings = Analyze(
            [ItemWith("111", Mod("ModA", incompatible: ["ModB"])), ItemWith("222", Mod("ModB"))],
            configured: ["111", "222"],
            enabled: ["ModA", "ModB"]);

        await Assert.That(findings.Any(f => f.Kind == ModCompatKind.IncompatiblePresent && f.Subject == "ModB")).IsTrue();
    }

    [Test]
    public async Task Incompatible_markers_are_normalized_for_comparison()
    {
        // PZ writes incompatible values with a leading '\' and trailing '+'/'-' (research §6); they still match the
        // bare installed id.
        IReadOnlyList<ModCompatFinding> findings = Analyze(
            [ItemWith("111", Mod("ModA", incompatible: ["\\ModB+"])), ItemWith("222", Mod("ModB"))],
            configured: ["111", "222"],
            enabled: ["ModA", "ModB"]);

        await Assert.That(findings.Any(f => f.Kind == ModCompatKind.IncompatiblePresent && f.Subject == "ModB")).IsTrue();
    }

    [Test]
    public async Task An_incompatible_mod_that_is_not_enabled_produces_no_finding()
    {
        IReadOnlyList<ModCompatFinding> findings = Analyze(
            [ItemWith("111", Mod("ModA", incompatible: ["ModB"])), ItemWith("222", Mod("ModB"))],
            configured: ["111", "222"],
            enabled: ["ModA"]);

        await Assert.That(findings.Any(f => f.Kind == ModCompatKind.IncompatiblePresent)).IsFalse();
    }
}
