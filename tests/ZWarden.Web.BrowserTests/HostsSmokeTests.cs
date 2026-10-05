using Microsoft.Playwright;
using ZWarden.Domain.Ids;
using static Microsoft.Playwright.Assertions;

namespace ZWarden.Web.BrowserTests;

/// <summary>
/// The Hosts smoke test (#342): the Enroll host island on the static Hosts page mints a one-time token in a real
/// browser, the old /enrollment URL lands on the open sheet, and Remove host (#363) takes an empty Host off the page.
/// </summary>
[ClassDataSource<BrowserHost>(Shared = SharedType.PerTestSession)]
public sealed class HostsSmokeTests(BrowserHost host)
{
    [Test]
    public async Task Enroll_host_generates_a_token_and_done_closes_the_sheet()
    {
        await using BrowserSession session = await host.OpenAsync("/hosts");

        await session.Page.ClickAsync("[data-action=enroll-host-open]");
        ILocator sheet = session.Page.Locator("[data-enroll-host-sheet]");
        await Expect(sheet).ToBeVisibleAsync();
        await session.FillAsync("#enroll-label", "smoke-enroll");
        await session.Page.ClickAsync("[data-action=step-finish]");

        await Expect(sheet.Locator("[data-enrollment-token]")).ToContainTextAsync("zwe_");
        await Expect(sheet.Locator("[data-enrollment-env]")).ToContainTextAsync("ZWARDEN_ENROLLMENT_SECRET=zwe_");
        await Expect(sheet.Locator("[data-enrollment-row]", new() { HasText = "smoke-enroll" })).ToBeVisibleAsync();
        await Expect(sheet.Locator("[data-enrollment-waiting]")).ToBeVisibleAsync();

        await session.Page.ClickAsync("[data-action=step-finish]");
        await Expect(sheet).ToHaveCountAsync(0);
        await session.AssertNoErrorsAsync();
    }

    [Test]
    public async Task Remove_host_takes_an_empty_host_off_the_page()
    {
        AgentId agentId = await host.SeedConnectedHostAsync("smoke-remove");
        await using BrowserSession session = await host.OpenAsync("/hosts");
        ILocator card = session.Page.Locator($"#host-{agentId}");
        await Expect(card.Locator("[data-circuit=on]")).ToHaveCountAsync(1); // the card's island is interactive

        await card.Locator("[data-action=remove-host-open]").ClickAsync();
        ILocator dialog = session.Page.Locator("[data-remove-host-dialog]");
        await Expect(dialog).ToHaveCountAsync(1); // one portal host on the page, so one dialog (#363)
        await Expect(dialog).ToContainTextAsync("Remove smoke-remove?");
        await dialog.Locator("[data-action=remove-host]").ClickAsync();

        await Expect(card).ToHaveCountAsync(0);
        await session.AssertNoErrorsAsync();
    }

    [Test]
    public async Task The_old_enrollment_url_opens_the_sheet_on_hosts()
    {
        await using BrowserSession session = await host.OpenAsync("/enrollment");

        await Expect(session.Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/hosts\\?enroll=1$"));
        await Expect(session.Page.Locator("[data-enroll-host-sheet]")).ToBeVisibleAsync();
        await session.AssertNoErrorsAsync();
    }
}
