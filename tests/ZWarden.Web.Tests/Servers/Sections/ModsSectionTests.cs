using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Configuration;
using ZWarden.Application.Mods;
using ZWarden.Application.Servers;
using ZWarden.Application.Workshop;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Web.Components.Pages.Servers;

namespace ZWarden.Web.Tests.Servers.Sections;

/// <summary>
/// The Mods section as Variant B (#292): one table in load order — a row per Workshop item, "other mod" rows for ids no
/// item provides — with status chips, Remove/Undo, ▲/▼, parts and mod.info warnings, and a sticky bar whose "Restart
/// to apply" carries the chosen countdown. Each change is a drift-checked config apply through the real mod manager;
/// the restart loads mods and pulls Workshop updates but never runs the game update.
/// </summary>
public sealed class ModsSectionTests
{
    [Test]
    public async Task Rescan_enqueues_a_discovery()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("discoverable");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "mods");

        await cut.Find("[data-action=mod-refresh]").ClickAsync(new());

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.ModDiscovery) is not null);
        await Assert.That(cut.Find("[data-mod-message]").TextContent).Contains("Re-scan started");
    }

    [Test]
    public async Task Add_installs_the_typed_item_with_its_description_mod_id()
    {
        // #291: the add field is one-click Install too — WorkshopItems= and the description's Mod ID in one apply.
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(services =>
            services.AddSingleton<IWorkshopMetadataService>(new OneItemPreview("200", "Mod ID: NewMod")));
        ServerId serverId = await harness.SeedServerAsync("addable");
        harness.SeedInventory(serverId, installed: [], workshop: ["100"], enabled: []);
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "mods");

        await InteractivePageHarness.TypeAsync(cut, "mod-workshop-id", "200");
        await cut.Find("[data-mod-add] [data-action=mod-install]").ClickAsync(new());

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.ConfigApply) is not null);
        Operation op = harness.FirstOperation(serverId, OperationKind.ConfigApply)!;
        await Assert.That(op.IsMutating).IsTrue();
        await Assert.That(Edits(harness, serverId)).IsEquivalentTo(["WorkshopItems=100;200", "Mods=NewMod"]);
    }

    [Test]
    public async Task Each_item_is_listed_once_in_load_order_with_its_title_and_status()
    {
        // The "badly broken" page listed every item twice; the table lists it once, where it loads.
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("ordered");
        await harness.SeedModStateAsync(
            serverId, booted: (["100", "200"], ["B", "A"]), configured: (["100", "200"], ["B", "A"]),
            ("100", [], ["A"], true), ("200", [], ["B"], true));
        SeedDisk(harness, serverId, ["100", "200"], ["B", "A"], ("100", "A"), ("200", "B"));
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "mods");

        cut.WaitForState(() => cut.FindAll("[data-mod-row]").Count == 2);
        await Assert.That(string.Join("|", cut.FindAll("[data-mod-row]").Select(r => r.GetAttribute("data-workshop-id"))))
            .IsEqualTo("200|100");
        await Assert.That(cut.Find("[data-mod-row][data-workshop-id='200'] [data-mod-title]").TextContent).IsEqualTo("Item 200");
        await Assert.That(cut.FindAll("[data-mod-status=Active]").Count).IsEqualTo(2);
        await Assert.That(cut.FindAll("[data-mod-pending-bar]")).IsEmpty();
    }

    [Test]
    public async Task A_pending_install_shows_its_status_and_undo_takes_it_back_out()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("undoable");
        await harness.SeedModStateAsync(
            serverId, booted: (["100"], ["A"]), configured: (["100", "200"], ["A", "B"]),
            ("100", [], ["A"], true), ("200", ["B"], [], false));
        SeedDisk(harness, serverId, ["100", "200"], ["A", "B"], ("100", "A"));
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "mods");

        cut.WaitForState(() => cut.FindAll("[data-mod-status=InstallsOnRestart]").Count == 1);
        await cut.Find("[data-mod-actions][data-row-key='item:200'] [data-action=mod-undo]").ClickAsync(new());

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.ConfigApply) is not null);
        await Assert.That(Edits(harness, serverId)).IsEquivalentTo(["WorkshopItems=100", "Mods=A"]);
    }

    [Test]
    public async Task A_pending_removal_stays_in_the_table_struck_through_with_undo()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("removed");
        await harness.SeedModStateAsync(
            serverId, booted: (["100", "200"], ["A", "B"]), configured: (["100"], ["A"]),
            ("100", [], ["A"], true), ("200", [], ["B"], true));
        SeedDisk(harness, serverId, ["100"], ["A"], ("100", "A"), ("200", "B"));
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "mods");

        cut.WaitForState(() => cut.FindAll("[data-mod-status=RemovedOnRestart]").Count == 1);
        await Assert.That(cut.Find("[data-mod-row][data-workshop-id='200'] [data-mod-title]").ClassName).Contains("line-through");
        await cut.Find("[data-mod-actions][data-row-key='item:200'] [data-action=mod-undo]").ClickAsync(new());

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.ConfigApply) is not null);
        await Assert.That(Edits(harness, serverId)).IsEquivalentTo(["WorkshopItems=100;200", "Mods=A;B"]);
    }

    [Test]
    public async Task Remove_takes_out_the_item_and_its_mods_in_one_apply()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("removable");
        await harness.SeedModStateAsync(
            serverId, booted: (["100", "200"], ["A", "B"]), configured: (["100", "200"], ["A", "B"]),
            ("100", [], ["A"], true), ("200", [], ["B"], true));
        SeedDisk(harness, serverId, ["100", "200"], ["A", "B"], ("100", "A"), ("200", "B"));
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "mods");

        cut.WaitForState(() => cut.FindAll("[data-mod-row]").Count == 2);
        await cut.Find("[data-mod-actions][data-row-key='item:200'] [data-action=mod-remove]").ClickAsync(new());

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.ConfigApply) is not null);
        await Assert.That(Edits(harness, serverId)).IsEquivalentTo(["WorkshopItems=100", "Mods=A"]);
    }

    [Test]
    public async Task Moving_an_item_down_moves_all_of_its_parts_past_the_next_item()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("reorderable");
        await harness.SeedModStateAsync(
            serverId, booted: (["100", "200"], ["P1", "P2", "X"]), configured: (["100", "200"], ["P1", "P2", "X"]),
            ("100", [], ["P1", "P2"], true), ("200", [], ["X"], true));
        SeedDisk(harness, serverId, ["100", "200"], ["P1", "P2", "X"], ("100", "P1"), ("100", "P2"), ("200", "X"));
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "mods");

        cut.WaitForState(() => cut.FindAll("[data-mod-row]").Count == 2);
        await Assert.That(cut.FindAll("[data-mod-order][data-row-key='item:100'] [data-action=mod-moveup]")).IsEmpty();
        await cut.Find("[data-mod-order][data-row-key='item:100'] [data-action=mod-movedown]").ClickAsync(new());

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.ConfigApply) is not null);
        await Assert.That(Edits(harness, serverId)).IsEquivalentTo(["Mods=X;P1;P2"]);
    }

    [Test]
    public async Task An_enabled_id_from_no_tracked_item_is_its_own_row_and_remove_turns_it_off()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("othermod");
        await harness.SeedModStateAsync(
            serverId, booted: (["100"], ["A", "LocalMod"]), configured: (["100"], ["A", "LocalMod"]), ("100", [], ["A"], true));
        SeedDisk(harness, serverId, ["100"], ["A", "LocalMod"], ("100", "A"));
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "mods");

        cut.WaitForState(() => cut.FindAll("[data-mod-row][data-row-key='mod:LocalMod']").Count == 1);
        await Assert.That(cut.Find("[data-mod-row][data-row-key='mod:LocalMod']").TextContent).Contains("Not from a tracked Workshop item");
        await cut.Find("[data-mod-actions][data-row-key='mod:LocalMod'] [data-action=mod-remove]").ClickAsync(new());

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.ConfigApply) is not null);
        await Assert.That(Edits(harness, serverId)).IsEquivalentTo(["Mods=A"]);
    }

    [Test]
    public async Task A_multi_part_item_expands_to_its_parts_and_saving_turns_on_the_ticked_ones()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("multipart");
        await harness.SeedModStateAsync(
            serverId, booted: (["100"], ["P1"]), configured: (["100"], ["P1"]), ("100", [], ["P1", "P2"], true));
        SeedDisk(harness, serverId, ["100"], ["P1"], ("100", "P1"), ("100", "P2"));
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "mods");

        cut.WaitForState(() => cut.FindAll("[data-action=mod-parts-toggle]").Count == 1);
        await Assert.That(cut.Find("[data-action=mod-parts-toggle]").TextContent).Contains("1 of 2 parts on");
        await Assert.That(cut.FindAll("[data-mod-pick-parts]")).IsEmpty();
        await cut.Find("[data-action=mod-parts-toggle]").ClickAsync(new());
        cut.WaitForState(() => cut.FindAll("[data-mod-pick-parts]").Count == 1);
        await cut.Find("[data-mod-parts-option][data-mod-id='P2'] [role=checkbox]").ClickAsync(new());
        await cut.Find("[data-action=mod-parts-save]").ClickAsync(new());

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.ConfigApply) is not null);
        await Assert.That(Edits(harness, serverId)).IsEquivalentTo(["Mods=P1;P2"]);
    }

    [Test]
    public async Task A_wrong_guess_asks_to_pick_parts_and_saving_swaps_in_the_real_part()
    {
        // The Workshop page said "Guess"; the download provides "Real". The notice names both, and the picker starts
        // on the real part, so one Save fixes Mods= in one apply.
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("pickparts");
        await harness.SeedModStateAsync(
            serverId, booted: (["200"], ["Guess"]), configured: (["200"], ["Guess"]), ("200", ["Guess"], ["Real"], true));
        SeedDisk(harness, serverId, ["200"], ["Guess"], ("200", "Real"));
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "mods");

        cut.WaitForState(() => cut.FindAll("[data-mod-pick-parts]").Count == 1);
        await Assert.That(cut.FindAll("[data-mod-status=PickParts]").Count).IsEqualTo(1);
        string notice = cut.Find("[data-mod-pick-parts]").TextContent;
        await Assert.That(notice).Contains("Guess");
        await Assert.That(notice).Contains("Real");
        await cut.Find("[data-mod-pick-parts] [data-action=mod-parts-save]").ClickAsync(new());

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.ConfigApply) is not null);
        await Assert.That(Edits(harness, serverId)).IsEquivalentTo(["Mods=Real"]);
    }

    [Test]
    public async Task A_missing_requirement_shows_as_a_warning_on_the_row()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("needslib");
        await harness.SeedModStateAsync(
            serverId, booted: (["100"], ["A"]), configured: (["100"], ["A"]), ("100", [], ["A"], true));
        harness.SeedInventory(
            serverId, installed: [new InstalledWorkshopItem("100", [new InstalledMod("A", null, Requires: ["Lib"])])],
            workshop: ["100"], enabled: ["A"]);
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "mods");

        cut.WaitForState(() => cut.FindAll("[data-mod-warnings]").Count == 1);
        await Assert.That(cut.Find("[data-mod-row][data-workshop-id='100'] [data-mod-warnings]").TextContent)
            .Contains("A needs Lib, which isn't installed.");
    }

    [Test]
    public async Task The_pending_bar_counts_the_changes_and_restarts_with_the_chosen_countdown()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("restartable-mods");
        await harness.SeedModStateAsync(
            serverId, booted: (["100", "300"], ["A", "C"]), configured: (["100", "200"], ["A", "B"]),
            ("100", [], ["A"], true), ("200", ["B"], [], false), ("300", [], ["C"], true));
        SeedDisk(harness, serverId, ["100", "200"], ["A", "B"], ("100", "A"), ("300", "C"));
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "mods");

        cut.WaitForState(() => cut.FindAll("[data-mod-pending-bar]").Count == 1);
        await Assert.That(cut.Find("[data-mod-pending-count]").TextContent).Contains("2 changes waiting for a restart");
        await Assert.That(cut.Find("[data-mod-pending-summary]").TextContent).IsEqualTo("+ Item 200 · − Item 300");
        await cut.Find("#mod-restart-countdown").ChangeAsync(new() { Value = "1m" });
        await cut.Find("[data-action=mod-restart]").ClickAsync(new());

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.RestartServer) is not null);
        await Assert.That(harness.FirstOperation(serverId, OperationKind.UpdateServer)).IsNull();
        GracefulRestartPayload plan = GracefulRestartPayload.FromJson(harness.Payload(serverId, OperationKind.RestartServer)!);
        await Assert.That(plan.WarningLeadSeconds).IsEquivalentTo([60, 30, 10]);
        await Assert.That(cut.Find("[data-mod-message]").TextContent).Contains("Workshop updates download as the server boots");
    }

    [Test]
    public async Task With_nothing_pending_a_restart_still_pulls_workshop_updates_with_the_default_warning()
    {
        // #273: the checksum-mismatch fix — restart to re-fetch WorkshopItems=, never the game update.
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("update-mods");
        harness.SeedInventory(serverId, installed: [], workshop: ["100"], enabled: []);
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "mods");

        await cut.Find("[data-action=mod-update-restart]").ClickAsync(new());

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.RestartServer) is not null);
        await Assert.That(harness.FirstOperation(serverId, OperationKind.UpdateServer)).IsNull();
        GracefulRestartPayload plan = GracefulRestartPayload.FromJson(harness.Payload(serverId, OperationKind.RestartServer)!);
        await Assert.That(plan.WarningLeadSeconds).IsEquivalentTo([300, 60, 30, 10]);
    }

    private static void SeedDisk(
        InteractivePageHarness harness,
        ServerId serverId,
        IReadOnlyList<string> workshop,
        IReadOnlyList<string> enabled,
        params (string WorkshopId, string ModId)[] mods) =>
        harness.SeedInventory(
            serverId,
            installed:
            [
                .. mods.GroupBy(m => m.WorkshopId).Select(g =>
                    new InstalledWorkshopItem(g.Key, [.. g.Select(m => new InstalledMod(m.ModId, null))])),
            ],
            workshop,
            enabled);

    private static IEnumerable<string> Edits(InteractivePageHarness harness, ServerId serverId) =>
        ConfigApplyPayload.FromJson(harness.Payload(serverId, OperationKind.ConfigApply)!).Edits.Select(e => $"{e.Path}={e.Value}");

    private sealed class OneItemPreview(string workshopId, string description) : IWorkshopMetadataService
    {
        public Task<WorkshopPreview> ResolveAsync(
            UserId actor, ServerId server, string input, CancellationToken cancellationToken = default) =>
            Task.FromResult(WorkshopPreview.OfItem(new WorkshopItemMetadata(workshopId, Found: true, Title: "t", Description: description)));
    }
}
