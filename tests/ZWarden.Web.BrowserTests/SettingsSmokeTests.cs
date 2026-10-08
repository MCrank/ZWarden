using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ZWarden.Web.BrowserTests;

/// <summary>The Settings smoke tests: the owner saves, then clears, the Workshop search key (#299), and the sub-nav
/// switches sections in the circuit, back/forward included, with a bookmarkable URL per section (#344).</summary>
[ClassDataSource<BrowserHost>(Shared = SharedType.PerTestSession)]
public sealed partial class SettingsSmokeTests(BrowserHost host)
{
    [Test]
    public async Task Settings_saves_then_clears_the_workshop_search_key()
    {
        await using BrowserSession session = await host.OpenAsync("/settings/integrations");
        ILocator workshop = session.Page.Locator("[data-settings-workshop]");

        await session.FillAsync("#workshop-key", "ABCDEF0123456789ABCDEF0123456789");
        await workshop.GetByRole(AriaRole.Button, new() { Name = "Save key" }).ClickAsync();
        await Expect(workshop).ToContainTextAsync("A search key is configured");

        await workshop.GetByRole(AriaRole.Button, new() { Name = "Clear key" }).ClickAsync();
        await Expect(workshop).ToContainTextAsync("No search key configured");
        await session.AssertNoErrorsAsync();
    }

    [Test]
    public async Task The_sub_nav_switches_sections_in_place_and_each_url_opens_its_section()
    {
        await using BrowserSession session = await host.OpenAsync("/settings");
        int pageRequests = 0;
        session.Page.Request += (_, request) =>
        {
            if (new Uri(request.Url).AbsolutePath.StartsWith("/settings", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref pageRequests);
            }
        };

        await session.Page.ClickAsync("[data-settings-nav-item=security]");
        await Expect(session.Page.Locator("[data-settings-security]")).ToBeVisibleAsync();
        await session.Page.ClickAsync("[data-settings-nav-item=about]");
        await Expect(session.Page.Locator("[data-settings-about]")).ToBeVisibleAsync();
        await Expect(session.Page).ToHaveURLAsync(AboutUrl());
        await Expect(session.Page.Locator("[data-settings-nav-item=about]")).ToHaveClassAsync(Active());

        await session.Page.GoBackAsync();
        await Expect(session.Page.Locator("[data-settings-security]")).ToBeVisibleAsync();
        await session.Page.GoForwardAsync();
        await Expect(session.Page.Locator("[data-settings-about]")).ToBeVisibleAsync();
        await session.WaitForCircuitAsync();
        await Assert.That(Volatile.Read(ref pageRequests)).IsEqualTo(0).Because("a section switch must not ask the server for the page");

        await session.Page.ReloadAsync();
        await session.WaitForCircuitAsync();
        await Expect(session.Page.Locator("[data-settings-about]")).ToBeVisibleAsync();
        await session.AssertNoErrorsAsync();
    }

    [Test]
    public async Task Renaming_the_instance_updates_the_sidebar_without_a_restart()
    {
        // #345: the shell is static, so the page refreshes it after a save; the circuit (and its message) stays.
        await using BrowserSession session = await host.OpenAsync("/settings");
        ILocator label = session.Page.Locator("[data-shell-menu-trigger=workspace] .zw-t1");
        try
        {
            await session.FillAsync("#instance-name", "Knox Ops");
            await session.Page.ClickAsync("[data-action=instance-name-save]");
            await Expect(session.Page.Locator("[data-settings-instance-saved]")).ToBeVisibleAsync();
            await Expect(label).ToHaveTextAsync("Knox Ops");
            await Expect(session.Page).ToHaveTitleAsync("Settings · Knox Ops");
        }
        finally
        {
            // The host is shared by the session's tests: put the configured name back.
            await session.Page.ClickAsync("[data-action=instance-name-reset]");
            await Expect(label).ToHaveTextAsync("ZWarden");
        }

        await session.AssertNoErrorsAsync();
    }

    [GeneratedRegex(@"/settings/about$")]
    private static partial Regex AboutUrl();

    [GeneratedRegex(@"\bactive\b")]
    private static partial Regex Active();
}
