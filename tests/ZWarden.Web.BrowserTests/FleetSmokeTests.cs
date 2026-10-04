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
    public async Task The_deploy_deep_link_opens_the_sheet()
    {
        await using BrowserSession session = await host.OpenAsync("/servers?deploy=1");

        await Expect(session.Page.Locator("[data-deploy-server-sheet]")).ToBeVisibleAsync();
        await session.AssertNoErrorsAsync();
    }
}
