using Microsoft.Playwright;
using ZWarden.Domain.Ids;
using static Microsoft.Playwright.Assertions;

namespace ZWarden.Web.BrowserTests;

/// <summary>
/// The Fleet smoke test (#338): the Deploy server island on the static Fleet page walks its steps and deploys in a
/// real browser. The enhanced refresh after Deploy must add the row to the board while the island keeps its toast.
/// </summary>
[ClassDataSource<BrowserHost>(Shared = SharedType.PerTestSession)]
public sealed class FleetSmokeTests(BrowserHost host)
{
    [Test]
    public async Task Deploy_server_walks_the_steps_and_the_new_row_appears()
    {
        AgentId agent = await host.SeedConnectedHostAsync("smoke-host");
        await using BrowserSession session = await host.OpenAsync("/servers");

        await session.Page.ClickAsync("[data-action=deploy-server-open]");
        ILocator sheet = session.Page.Locator("[data-deploy-server-sheet]");
        await Expect(sheet).ToBeVisibleAsync();
        await sheet.Locator("#deploy-host").SelectOptionAsync(agent.ToString());
        await session.FillAsync("#deploy-name", "smoke-deployed");
        await session.Page.ClickAsync("[data-action=step-next]");
        await Expect(sheet.Locator("[data-deploy-step=game-version]")).ToBeVisibleAsync();
        await session.Page.ClickAsync("[data-action=step-next]");
        await Expect(sheet.Locator("[data-deploy-step=memory]")).ToBeVisibleAsync();
        await session.Page.ClickAsync("[data-action=step-finish]");

        await Expect(sheet).ToHaveCountAsync(0);
        await Expect(session.Page.GetByText("smoke-deployed is deploying on smoke-host")).ToBeVisibleAsync();
        await Expect(session.Page.Locator("[data-fleet-board]")).ToContainTextAsync("smoke-deployed");
        await session.AssertNoErrorsAsync();
    }

    [Test]
    public async Task A_host_with_no_servers_shows_as_an_empty_host_row()
    {
        // #342 live pass: a freshly enrolled Host belongs on the Fleet before anything is deployed to it.
        AgentId agent = await host.SeedConnectedHostAsync("idle-host");
        await using BrowserSession session = await host.OpenAsync("/servers");
        ILocator board = session.Page.Locator("[data-fleet-board]");
        await Expect(board).ToHaveAttributeAsync("data-fleet-ready", "on");

        ILocator row = board.Locator($"[data-fleet-host-row='{agent}']");
        await Expect(row).ToBeVisibleAsync();
        await Expect(row.Locator("[data-fleet-host-count]")).ToHaveTextAsync("0 servers");
        await session.AssertNoErrorsAsync();
    }

    [Test]
    public async Task Collapse_all_hides_the_servers_and_survives_a_reload_until_expand_all()
    {
        // #340: Servers sit under Host rows; collapse state is remembered per browser (local-prefs.js).
        await host.SeedServerAsync("tree-orphan"); // an unknown Agent, so the Unassigned group
        await using BrowserSession session = await host.OpenAsync("/servers");
        ILocator board = session.Page.Locator("[data-fleet-board]");
        ILocator server = board.Locator("[data-server-link]", new() { HasText = "tree-orphan" });
        await Expect(board).ToHaveAttributeAsync("data-fleet-ready", "on");
        await Expect(board.Locator("[data-fleet-host-row=unassigned]")).ToBeVisibleAsync();
        await Expect(server).ToBeVisibleAsync();

        await session.Page.ClickAsync("[data-fleet-expand=none]");
        await Expect(server).ToHaveCountAsync(0);

        await session.Page.ReloadAsync();
        await session.WaitForCircuitAsync();
        await Expect(board).ToHaveAttributeAsync("data-fleet-ready", "on");
        await Expect(board.Locator("[data-fleet-host-row=unassigned]")).ToBeVisibleAsync();
        await Expect(server).ToHaveCountAsync(0);

        await session.Page.ClickAsync("[data-fleet-expand=all]");
        await Expect(server).ToBeVisibleAsync();
        await session.AssertNoErrorsAsync();
    }

    [Test]
    public async Task The_adopt_banner_adopts_a_discovered_container_and_clears()
    {
        // #339: the banner and the sheet are separate islands in one circuit; Adopt in one opens the other.
        AgentId agent = await host.SeedConnectedHostAsync("adopt-host");
        host.Discover(agent, ServerId.New(), ZWarden.Domain.Servers.ServerRunState.Running);
        await using BrowserSession session = await host.OpenAsync("/servers");
        ILocator banner = session.Page.Locator("[data-unmanaged-banner]");
        await Expect(banner).ToContainTextAsync("adopt-host");

        await banner.Locator("[data-action=adopt-open]").ClickAsync();
        ILocator sheet = session.Page.Locator("[data-deploy-server-sheet]");
        await Expect(sheet.Locator("[data-deploy-step=adopt]")).ToBeVisibleAsync();
        await sheet.Locator("[data-adopt-option]", new() { HasText = "adopt-host" }).Locator("[role=radio]").ClickAsync();
        await session.FillAsync("#adopt-name", "smoke-adopted");
        await session.Page.ClickAsync("[data-action=step-finish]");

        await Expect(sheet).ToHaveCountAsync(0);
        await Expect(session.Page.Locator("[data-fleet-board]")).ToContainTextAsync("smoke-adopted");
        await Expect(banner).ToHaveCountAsync(0);
        await session.AssertNoErrorsAsync();
    }

    [Test]
    public async Task The_deploy_deep_link_opens_the_sheet()
    {
        await using BrowserSession session = await host.OpenAsync("/servers?deploy=1");

        await Expect(session.Page.Locator("[data-deploy-server-sheet]")).ToBeVisibleAsync();
        await session.AssertNoErrorsAsync();
    }
}
