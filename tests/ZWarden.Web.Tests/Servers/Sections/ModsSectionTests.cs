using Bunit;
using ZWarden.Application.Configuration;
using ZWarden.Application.Mods;
using ZWarden.Application.Servers;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Servers;
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
    public async Task After_a_change_the_table_updates_by_itself_once_the_new_lists_are_recorded()
    {
        // Live pass: the apply is enqueued, not done, so the table used to show the old lists until the page was left
        // and reopened. Now it re-reads until the discovery after the apply records the new lists.
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("self-refresh");
        await harness.SeedModStateAsync(
            serverId, booted: (["100", "200"], ["A", "B"]), configured: (["100", "200"], ["A", "B"]),
            ("100", [], ["A"], true), ("200", [], ["B"], true));
        SeedDisk(harness, serverId, ["100", "200"], ["A", "B"], ("100", "A"), ("200", "B"));
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "mods");

        cut.WaitForState(() => cut.FindAll("[data-mod-status=Active]").Count == 2);
        await cut.Find("[data-mod-actions][data-row-key='item:200'] [data-action=mod-remove]").ClickAsync(new());
        cut.WaitForState(() => cut.FindAll("[data-mod-refreshing]").Count == 1);
        await Assert.That(cut.FindAll("[data-mod-status=RemovedOnRestart]")).IsEmpty();

        await harness.ObserveModConfigAsync(serverId, ["100"], ["A"]);

        cut.WaitForState(() => cut.FindAll("[data-mod-status=RemovedOnRestart]").Count == 1, TimeSpan.FromSeconds(10));
        cut.WaitForState(() => cut.FindAll("[data-mod-refreshing]").Count == 0, TimeSpan.FromSeconds(5));
        await Assert.That(cut.Find("[data-mod-pending-count]").TextContent).Contains("1 change waiting");
    }

    [Test]
    public async Task After_the_restart_boots_the_rows_turn_active_by_themselves()
    {
        // Live pass: after Restart to apply the row kept "Changes on restart" until the page was reopened. The boot's
        // discovery lands minutes after the click, so the open section keeps re-reading.
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("boots");
        await harness.SeedModStateAsync(
            serverId, booted: (["100"], ["A"]), configured: (["100", "200"], ["A", "B"]),
            ("100", [], ["A"], true), ("200", ["B"], [], false));
        SeedDisk(harness, serverId, ["100", "200"], ["A", "B"], ("100", "A"));
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "mods");
        cut.WaitForState(() => cut.FindAll("[data-mod-status=InstallsOnRestart]").Count == 1);

        await harness.RecordBootAsync(serverId);

        cut.WaitForState(() => cut.FindAll("[data-mod-status=Active]").Count == 2, TimeSpan.FromSeconds(15));
        await Assert.That(cut.FindAll("[data-mod-pending-bar]")).IsEmpty();
    }

    [Test]
    public async Task A_half_ticked_parts_picker_survives_the_background_refresh()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("keeps-ticks");
        await harness.SeedModStateAsync(
            serverId, booted: (["100"], ["P1"]), configured: (["100"], ["P1"]), ("100", [], ["P1", "P2"], true));
        SeedDisk(harness, serverId, ["100"], ["P1"], ("100", "P1"), ("100", "P2"));
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "mods");
        cut.WaitForState(() => cut.FindAll("[data-action=mod-parts-toggle]").Count == 1);
        await cut.Find("[data-action=mod-parts-toggle]").ClickAsync(new());
        cut.WaitForState(() => cut.FindAll("[data-mod-pick-parts]").Count == 1);
        await cut.Find("[data-mod-parts-option][data-mod-id='P2'] [role=checkbox]").ClickAsync(new());

        await Task.Delay(TimeSpan.FromSeconds(6)); // longer than one idle refresh

        await Assert.That(cut.Find("[data-mod-parts-option][data-mod-id='P2'] [role=checkbox]").GetAttribute("aria-checked")).IsEqualTo("true");
        await Assert.That(cut.FindAll("[data-mod-pick-parts]").Count).IsEqualTo(1);
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
    public async Task Picking_parts_starts_empty_and_blocks_a_part_that_conflicts_with_a_ticked_one()
    {
        // Live pass (Equipment UI): every part was pre-ticked, so one Save turned on both builds of the same mod.
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("conflicting-parts");
        await harness.SeedModStateAsync(
            serverId, booted: (["100"], []), configured: (["100"], []), ("100", [], ["EQUIPMENT_UI", "EQUIPMENT_UI_B42"], true));
        harness.SeedInventory(
            serverId,
            installed:
            [
                new InstalledWorkshopItem("100",
                [
                    new InstalledMod("EQUIPMENT_UI", null, Incompatible: ["EQUIPMENT_UI_B42"]),
                    new InstalledMod("EQUIPMENT_UI_B42", null),
                ]),
            ],
            workshop: ["100"], enabled: []);
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "mods");

        cut.WaitForState(() => cut.FindAll("[data-mod-pick-parts]").Count == 1);
        await Assert.That(cut.FindAll("[data-mod-parts-option] [role=checkbox][aria-checked=true]")).IsEmpty();
        await cut.Find("[data-mod-parts-option][data-mod-id='EQUIPMENT_UI_B42'] [role=checkbox]").ClickAsync(new());

        cut.WaitForState(() => cut.FindAll("[data-mod-parts-option][data-mod-id='EQUIPMENT_UI'] [data-mod-part-conflict]").Count == 1);
        await Assert.That(cut.Find("[data-mod-parts-option][data-mod-id='EQUIPMENT_UI'] [data-mod-part-conflict]").TextContent)
            .Contains("conflicts with EQUIPMENT_UI_B42");
        await Assert.That(cut.Find("[data-mod-parts-option][data-mod-id='EQUIPMENT_UI'] [role=checkbox]").HasAttribute("disabled")).IsTrue();
        await cut.Find("[data-action=mod-parts-save]").ClickAsync(new());

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.ConfigApply) is not null);
        await Assert.That(Edits(harness, serverId)).IsEquivalentTo(["Mods=EQUIPMENT_UI_B42"]);
    }

    [Test]
    public async Task Parts_already_on_together_despite_a_conflict_block_saving_until_one_is_unticked()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("conflict-on");
        await harness.SeedModStateAsync(
            serverId, booted: (["100"], ["A", "B"]), configured: (["100"], ["A", "B"]), ("100", [], ["A", "B"], true));
        harness.SeedInventory(
            serverId,
            installed: [new InstalledWorkshopItem("100", [new InstalledMod("A", null, Incompatible: ["B"]), new InstalledMod("B", null)])],
            workshop: ["100"], enabled: ["A", "B"]);
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "mods");

        cut.WaitForState(() => cut.FindAll("[data-action=mod-parts-toggle]").Count == 1);
        await cut.Find("[data-action=mod-parts-toggle]").ClickAsync(new());
        cut.WaitForState(() => cut.FindAll("[data-mod-pick-parts]").Count == 1);
        await Assert.That(cut.Find("[data-action=mod-parts-save]").HasAttribute("disabled")).IsTrue();

        await cut.Find("[data-mod-parts-option][data-mod-id='A'] [role=checkbox]").ClickAsync(new());

        cut.WaitForState(() => !cut.Find("[data-action=mod-parts-save]").HasAttribute("disabled"));
    }

    [Test]
    public async Task A_loaded_item_whose_parts_changed_says_it_changes_on_restart_not_active()
    {
        // Live pass: after Save parts the row still read Active until the restart, though the change was pending.
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("parts-pending");
        await harness.SeedModStateAsync(
            serverId, booted: (["100", "200"], ["P1", "X"]), configured: (["100", "200"], ["P1", "P2", "X"]),
            ("100", [], ["P1", "P2"], true), ("200", [], ["X"], true));
        SeedDisk(harness, serverId, ["100", "200"], ["P1", "P2", "X"], ("100", "P1"), ("100", "P2"), ("200", "X"));
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "mods");

        cut.WaitForState(() => cut.FindAll("[data-mod-status=PartsOnRestart]").Count == 1);
        await Assert.That(cut.Find("[data-mod-status=PartsOnRestart]").TextContent).Contains("Changes on restart");
        // The untouched item is still plainly Active.
        await Assert.That(cut.FindAll("[data-mod-status=Active]").Count).IsEqualTo(1);
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
    public async Task A_running_servers_workshop_update_shows_update_ready_and_its_own_bar_line()
    {
        // #275 D6: Steam's changes are listed apart from the operator's, and the one Restart to apply pulls them.
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("mod-updated");
        await harness.SetRunStateAsync(serverId, ServerRunState.Running);
        await harness.SeedModStateAsync(
            serverId, booted: (["100", "200"], ["A", "B"]), configured: (["100", "200"], ["A", "B"]),
            ("100", [], ["A"], true), ("200", [], ["B"], true));
        await harness.SeedWorkshopUpdateAsync(serverId, "200");
        SeedDisk(harness, serverId, ["100", "200"], ["A", "B"], ("100", "A"), ("200", "B"));
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "mods");

        cut.WaitForState(() => cut.FindAll("[data-mod-status=UpdateReady]").Count == 1);
        await Assert.That(cut.Find("[data-mod-status=UpdateReady]").TextContent.Trim()).IsEqualTo("Update ready");
        await Assert.That(cut.FindAll("[data-mod-pending-count]")).IsEmpty();
        await Assert.That(cut.Find("[data-mod-updates-count]").TextContent).Contains("1 mod update ready");
        await Assert.That(cut.Find("[data-mod-updates-summary]").TextContent).IsEqualTo("↑ Item 200");

        await cut.Find("[data-action=mod-restart]").ClickAsync(new());

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.RestartServer) is not null);
        await Assert.That(harness.FirstOperation(serverId, OperationKind.UpdateServer)).IsNull();
    }

    [Test]
    public async Task A_stopped_servers_workshop_update_says_it_updates_on_start_without_a_bar()
    {
        // #275 D7: a start pulls the update anyway, so there's nothing to restart for.
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("stopped-mod-updated");
        await harness.SetRunStateAsync(serverId, ServerRunState.Stopped);
        await harness.SeedModStateAsync(
            serverId, booted: (["100"], ["A"]), configured: (["100"], ["A"]), ("100", [], ["A"], true));
        await harness.SeedWorkshopUpdateAsync(serverId, "100");
        SeedDisk(harness, serverId, ["100"], ["A"], ("100", "A"));
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "mods");

        cut.WaitForState(() => cut.FindAll("[data-mod-status=UpdateReady]").Count == 1);
        await Assert.That(cut.Find("[data-mod-status=UpdateReady]").TextContent.Trim()).IsEqualTo("Updates on start");
        await Assert.That(cut.FindAll("[data-mod-pending-bar]")).IsEmpty();
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

    [Test]
    public async Task Unused_downloads_collapse_into_a_footer_and_reinstall_turns_a_one_mod_item_back_on()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("leftovers");
        await harness.SeedModStateAsync(
            serverId, booted: (["100"], ["A"]), configured: (["100"], ["A"]),
            ("100", [], ["A"], true), ("300", [], ["L"], true), ("400", [], ["P1", "P2"], true));
        SeedDisk(harness, serverId, ["100"], ["A"], ("100", "A"), ("300", "L"), ("400", "P1"), ("400", "P2"));
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "mods");

        cut.WaitForState(() => cut.FindAll("[data-mod-leftovers]").Count == 1);
        // Leftovers are not table rows; the footer counts them and lists them on demand.
        await Assert.That(cut.FindAll("[data-mod-row]").Count).IsEqualTo(1);
        await Assert.That(cut.Find("[data-action=mod-leftovers-toggle]").TextContent).Contains("2 unused downloads");
        await Assert.That(cut.FindAll("[data-mod-leftover]")).IsEmpty();
        await cut.Find("[data-action=mod-leftovers-toggle]").ClickAsync(new());
        cut.WaitForState(() => cut.FindAll("[data-mod-leftover]").Count == 2);
        await cut.Find("[data-mod-leftover][data-workshop-id='300'] [data-action=mod-reinstall]").ClickAsync(new());

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.ConfigApply) is not null);
        await Assert.That(Edits(harness, serverId)).IsEquivalentTo(["WorkshopItems=100;300", "Mods=A;L"]);
    }

    [Test]
    public async Task Deleting_one_unused_download_asks_first_then_enqueues_the_delete()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await SeedLeftoversAsync(harness, "delete-one");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "mods");

        await OpenLeftoversAsync(cut);
        await cut.Find("[data-mod-leftover][data-workshop-id='300'] [data-action=mod-leftover-delete]").ClickAsync(new());

        cut.WaitForState(() => cut.FindAll("[data-leftover-delete-dialog]").Count == 1);
        string dialog = cut.Find("[data-leftover-delete-dialog]").TextContent;
        await Assert.That(dialog).Contains("Delete the files for Item 300?");
        await Assert.That(dialog).Contains("Reinstall downloads them again");
        await Assert.That(harness.FirstOperation(serverId, OperationKind.DeleteWorkshopContent)).IsNull();

        await cut.Find("[data-action=leftover-delete-confirm]").ClickAsync(new());

        cut.WaitForState(() => harness.Payload(serverId, OperationKind.DeleteWorkshopContent) is not null);
        await Assert.That(WorkshopContentCommandPayload.FromJson(harness.Payload(serverId, OperationKind.DeleteWorkshopContent)!).WorkshopIds)
            .IsEquivalentTo(["300"]);
    }

    [Test]
    public async Task Delete_all_names_every_unused_download_and_deletes_them_in_one_operation()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await SeedLeftoversAsync(harness, "delete-all");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "mods");

        await OpenLeftoversAsync(cut);
        await cut.Find("[data-action=mod-leftovers-delete-all]").ClickAsync(new());

        cut.WaitForState(() => cut.FindAll("[data-leftover-delete-dialog]").Count == 1);
        string dialog = cut.Find("[data-leftover-delete-dialog]").TextContent;
        await Assert.That(dialog).Contains("Delete 2 unused downloads?");
        await Assert.That(dialog).Contains("Item 300");
        await Assert.That(dialog).Contains("Item 400");

        await cut.Find("[data-action=leftover-delete-confirm]").ClickAsync(new());

        cut.WaitForState(() => harness.Payload(serverId, OperationKind.DeleteWorkshopContent) is not null);
        await Assert.That(WorkshopContentCommandPayload.FromJson(harness.Payload(serverId, OperationKind.DeleteWorkshopContent)!).WorkshopIds)
            .IsEquivalentTo(["300", "400"]);
    }

    [Test]
    public async Task Cancelling_the_delete_dialog_deletes_nothing()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await SeedLeftoversAsync(harness, "delete-cancel");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "mods");

        await OpenLeftoversAsync(cut);
        await cut.Find("[data-mod-leftover][data-workshop-id='300'] [data-action=mod-leftover-delete]").ClickAsync(new());
        cut.WaitForState(() => cut.FindAll("[data-leftover-delete-dialog]").Count == 1);
        await cut.Find("[data-action=leftover-delete-cancel]").ClickAsync(new());

        cut.WaitForState(() => cut.FindAll("[data-leftover-delete-dialog]").Count == 0);
        await Assert.That(harness.FirstOperation(serverId, OperationKind.DeleteWorkshopContent)).IsNull();
    }

    private static async Task<ServerId> SeedLeftoversAsync(InteractivePageHarness harness, string name)
    {
        ServerId serverId = await harness.SeedServerAsync(name);
        await harness.SeedModStateAsync(
            serverId, booted: (["100"], ["A"]), configured: (["100"], ["A"]),
            ("100", [], ["A"], true), ("300", [], ["L"], true), ("400", [], ["P1"], true));
        SeedDisk(harness, serverId, ["100"], ["A"], ("100", "A"), ("300", "L"), ("400", "P1"));
        return serverId;
    }

    private static async Task OpenLeftoversAsync(IRenderedComponent<ServerDetail> cut)
    {
        cut.WaitForState(() => cut.FindAll("[data-mod-leftovers]").Count == 1);
        await cut.Find("[data-action=mod-leftovers-toggle]").ClickAsync(new());
        cut.WaitForState(() => cut.FindAll("[data-mod-leftover]").Count == 2);
    }

    [Test]
    public async Task Reinstalling_a_multi_mod_download_adds_the_item_only_so_its_parts_are_picked()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("leftover-multi");
        await harness.SeedModStateAsync(
            serverId, booted: (["100"], ["A"]), configured: (["100"], ["A"]),
            ("100", [], ["A"], true), ("400", [], ["P1", "P2"], true));
        SeedDisk(harness, serverId, ["100"], ["A"], ("100", "A"), ("400", "P1"), ("400", "P2"));
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "mods");

        cut.WaitForState(() => cut.FindAll("[data-mod-leftovers]").Count == 1);
        await cut.Find("[data-action=mod-leftovers-toggle]").ClickAsync(new());
        cut.WaitForState(() => cut.FindAll("[data-mod-leftover]").Count == 1);
        await cut.Find("[data-mod-leftover][data-workshop-id='400'] [data-action=mod-reinstall]").ClickAsync(new());

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.ConfigApply) is not null);
        await Assert.That(Edits(harness, serverId)).IsEquivalentTo(["WorkshopItems=100;400"]);
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
}
