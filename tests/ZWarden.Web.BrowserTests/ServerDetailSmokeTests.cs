using Microsoft.Playwright;
using ZWarden.Domain.Ids;

namespace ZWarden.Web.BrowserTests;

/// <summary>
/// The Server Detail smoke tier (#299, ADR 0046 Q4): each rail section opens in a real browser, does one
/// representative action, and the browser reports no error. Selectors are the pages' <c>data-*</c> hooks, so the same
/// tests hold while the page moves from static forms to circuit handlers.
/// </summary>
[ClassDataSource<BrowserHost>(Shared = SharedType.PerTestSession)]
public sealed class ServerDetailSmokeTests(BrowserHost host)
{
    [Test]
    public async Task Overview_starts_the_server()
    {
        ServerId serverId = await host.SeedServerAsync("smoke-overview");
        await using BrowserSession session = await host.OpenAsync($"/servers/{serverId}");

        await session.Page.ClickAsync("[data-action=server-start]");

        await Assertions.Expect(session.Page.Locator("[data-lifecycle-message]")).ToContainTextAsync("Start enqueued");
        await session.AssertNoErrorsAsync();
    }
}
