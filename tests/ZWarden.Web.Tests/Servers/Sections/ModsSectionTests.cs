using Bunit;
using ZWarden.Application.Mods;
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
        await using ServerDetailHarness harness = await ServerDetailHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("discoverable");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "mods");

        await cut.Find("[data-action=mod-refresh]").ClickAsync(new());

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.ModDiscovery) is not null);
        await Assert.That(cut.Find("[data-mod-message]").TextContent).Contains("Discovery started");
    }

    [Test]
    public async Task Add_enqueues_a_config_apply_touching_workshop_items()
    {
        await using ServerDetailHarness harness = await ServerDetailHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("addable");
        harness.SeedInventory(serverId, installed: [], workshop: ["100"], enabled: []);
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "mods");

        await ServerDetailHarness.TypeAsync(cut, "mod-workshop-id", "200");
        await cut.Find("[data-action=mod-add]").ClickAsync(new());

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.ConfigApply) is not null);
        Operation op = harness.FirstOperation(serverId, OperationKind.ConfigApply)!;
        await Assert.That(op.IsMutating).IsTrue();
        await Assert.That(op.CommandPayload).Contains("WorkshopItems");
        await Assert.That(op.CommandPayload).Contains("200");
    }

    [Test]
    public async Task Enable_applies_the_first_candidate_by_default()
    {
        await using ServerDetailHarness harness = await ServerDetailHarness.StartAsync();
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
        await using ServerDetailHarness harness = await ServerDetailHarness.StartAsync();
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
        await using ServerDetailHarness harness = await ServerDetailHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("restartable-mods");
        harness.SeedInventory(serverId, installed: [], workshop: ["100"], enabled: []);
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "mods");

        await cut.Find("[data-action=mod-restart]").ClickAsync(new());

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.RestartServer) is not null);
        await Assert.That(harness.FirstOperation(serverId, OperationKind.UpdateServer)).IsNull();
        await Assert.That(cut.Find("[data-mod-manage-message]").TextContent).Contains("Workshop updates download as the server boots");
    }
}
