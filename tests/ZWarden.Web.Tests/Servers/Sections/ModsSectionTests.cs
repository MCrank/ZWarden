using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Configuration;
using ZWarden.Application.Mods;
using ZWarden.Application.Workshop;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Web.Components.Pages.Servers;

namespace ZWarden.Web.Tests.Servers.Sections;

/// <summary>
/// The Mods section's discovery and management as circuit handlers (#299; formerly static form posts, F21/F22/#273).
/// Each change is a drift-checked config apply through the real mod manager; the restart loads mods and pulls
/// Workshop updates but never runs the game update.
/// </summary>
public sealed class ModsSectionTests
{
    [Test]
    public async Task Refresh_enqueues_a_discovery()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("discoverable");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "mods");

        await cut.Find("[data-action=mod-refresh]").ClickAsync(new());

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.ModDiscovery) is not null);
        await Assert.That(cut.Find("[data-mod-message]").TextContent).Contains("Discovery started");
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
        await Assert.That(ConfigApplyPayload.FromJson(op.CommandPayload!).Edits.Select(e => $"{e.Path}={e.Value}"))
            .IsEquivalentTo(["WorkshopItems=100;200", "Mods=NewMod"]);
    }

    [Test]
    public async Task A_pending_install_shows_its_status_and_undo_takes_it_back_out()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("undoable");
        await harness.SeedModStateAsync(
            serverId, booted: (["100"], ["A"]), configured: (["100", "200"], ["A", "B"]),
            ("100", [], ["A"], true), ("200", ["B"], [], false));
        harness.SeedInventory(
            serverId, installed: [new InstalledWorkshopItem("100", [new InstalledMod("A", null)])],
            workshop: ["100", "200"], enabled: ["A", "B"]);
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "mods");

        cut.WaitForState(() => cut.FindAll("[data-workshop-id='200'] [data-mod-status=InstallsOnRestart]").Count == 1);
        await cut.Find("[data-mod-workshop-row][data-workshop-id='200'] [data-action=mod-undo]").ClickAsync(new());

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.ConfigApply) is not null);
        await Assert.That(ConfigApplyPayload.FromJson(harness.Payload(serverId, OperationKind.ConfigApply)!).Edits.Select(e => $"{e.Path}={e.Value}"))
            .IsEquivalentTo(["WorkshopItems=100", "Mods=A"]);
    }

    [Test]
    public async Task A_pending_removal_is_listed_with_undo()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("removed");
        await harness.SeedModStateAsync(
            serverId, booted: (["100", "200"], ["A", "B"]), configured: (["100"], ["A"]),
            ("100", [], ["A"], true), ("200", [], ["B"], true));
        harness.SeedInventory(
            serverId,
            installed: [new InstalledWorkshopItem("100", [new InstalledMod("A", null)]), new InstalledWorkshopItem("200", [new InstalledMod("B", null)])],
            workshop: ["100"], enabled: ["A"]);
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "mods");

        cut.WaitForState(() => cut.FindAll("[data-mod-removed-row][data-workshop-id='200']").Count == 1);
        await cut.Find("[data-mod-removed-row][data-workshop-id='200'] [data-action=mod-undo]").ClickAsync(new());

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.ConfigApply) is not null);
        await Assert.That(ConfigApplyPayload.FromJson(harness.Payload(serverId, OperationKind.ConfigApply)!).Edits.Select(e => $"{e.Path}={e.Value}"))
            .IsEquivalentTo(["WorkshopItems=100;200", "Mods=A;B"]);
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
        harness.SeedInventory(
            serverId, installed: [new InstalledWorkshopItem("200", [new InstalledMod("Real", null)])],
            workshop: ["200"], enabled: ["Guess"]);
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "mods");

        cut.WaitForState(() => cut.FindAll("[data-mod-pick-parts]").Count == 1);
        string notice = cut.Find("[data-mod-pick-parts]").TextContent;
        await Assert.That(notice).Contains("Guess");
        await Assert.That(notice).Contains("Real");
        await cut.Find("[data-mod-pick-parts] [data-action=mod-parts-save]").ClickAsync(new());

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.ConfigApply) is not null);
        await Assert.That(ConfigApplyPayload.FromJson(harness.Payload(serverId, OperationKind.ConfigApply)!).Edits.Select(e => $"{e.Path}={e.Value}"))
            .IsEquivalentTo(["Mods=Real"]);
    }

    private sealed class OneItemPreview(string workshopId, string description) : IWorkshopMetadataService
    {
        public Task<WorkshopPreview> ResolveAsync(
            UserId actor, ServerId server, string input, CancellationToken cancellationToken = default) =>
            Task.FromResult(WorkshopPreview.OfItem(new WorkshopItemMetadata(workshopId, Found: true, Title: "t", Description: description)));
    }

    [Test]
    public async Task Enable_applies_the_first_candidate_by_default()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("enableable");
        harness.SeedInventory(
            serverId,
            installed: [new InstalledWorkshopItem("100", [new InstalledMod("ModB", null)])],
            workshop: ["100"],
            enabled: []);
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "mods");

        await cut.Find("[data-action=mod-enable]").ClickAsync(new());

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.ConfigApply) is not null);
        Operation op = harness.FirstOperation(serverId, OperationKind.ConfigApply)!;
        await Assert.That(op.CommandPayload).Contains("Mods");
        await Assert.That(op.CommandPayload).Contains("ModB");
    }

    [Test]
    public async Task Disable_enqueues_a_config_apply_touching_the_mods_list()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("disableable");
        harness.SeedInventory(
            serverId,
            installed: [new InstalledWorkshopItem("100", [new InstalledMod("ModA", null)])],
            workshop: ["100"],
            enabled: ["ModA", "ModB"]);
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "mods");

        await cut.Find("[data-mod-id=ModB] [data-action=mod-disable]").ClickAsync(new());

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.ConfigApply) is not null);
        await Assert.That(harness.FirstOperation(serverId, OperationKind.ConfigApply)!.CommandPayload).Contains("Mods");
    }

    [Test]
    public async Task Restart_to_apply_restarts_but_never_runs_the_game_update()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("restartable-mods");
        harness.SeedInventory(serverId, installed: [], workshop: ["100"], enabled: []);
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "mods");

        await cut.Find("[data-action=mod-restart]").ClickAsync(new());

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.RestartServer) is not null);
        await Assert.That(harness.FirstOperation(serverId, OperationKind.UpdateServer)).IsNull();
        await Assert.That(cut.Find("[data-mod-manage-message]").TextContent).Contains("Workshop updates download as the server boots");
    }
}
