using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ZWarden.Web.BrowserTests;

/// <summary>The Settings smoke test (#299): the owner saves, then clears, the Workshop search key.</summary>
[ClassDataSource<BrowserHost>(Shared = SharedType.PerTestSession)]
public sealed class SettingsSmokeTests(BrowserHost host)
{
    [Test]
    public async Task Settings_saves_then_clears_the_workshop_search_key()
    {
        await using BrowserSession session = await host.OpenAsync("/settings");
        ILocator workshop = session.Page.Locator("[data-settings-workshop]");

        await session.Page.FillAsync("#workshop-key", "ABCDEF0123456789ABCDEF0123456789");
        await workshop.GetByRole(AriaRole.Button, new() { Name = "Save key" }).ClickAsync();
        await Expect(workshop).ToContainTextAsync("A search key is configured");

        await workshop.GetByRole(AriaRole.Button, new() { Name = "Clear key" }).ClickAsync();
        await Expect(workshop).ToContainTextAsync("No search key configured");
        await session.AssertNoErrorsAsync();
    }
}
