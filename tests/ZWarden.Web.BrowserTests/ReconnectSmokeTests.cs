using Microsoft.Playwright;
using ZWarden.Domain.Ids;
using static Microsoft.Playwright.Assertions;

namespace ZWarden.Web.BrowserTests;

/// <summary>
/// The circuit reconnect dialog (#299): it stays out of the way on a live page, shows its state when the circuit is
/// lost (here a pause, which persists and evicts the circuit), and Resume brings the page back on a new circuit.
/// </summary>
[ClassDataSource<BrowserHost>(Shared = SharedType.PerTestSession)]
public sealed class ReconnectSmokeTests(BrowserHost host)
{
    [Test]
    public async Task A_paused_circuit_shows_the_dialog_and_resume_brings_the_page_back()
    {
        ServerId serverId = await host.SeedServerAsync("smoke-reconnect");
        await using BrowserSession session = await host.OpenAsync($"/servers/{serverId}");
        ILocator dialog = session.Page.Locator("#components-reconnect-modal");
        await Expect(dialog).Not.ToBeVisibleAsync();
        string before = await session.Page.GetAttributeAsync("[data-circuit]", "data-circuit-instance") ?? string.Empty;

        await session.Page.EvaluateAsync("() => Blazor.pauseCircuit()");

        await Expect(dialog).ToBeVisibleAsync();
        await Expect(dialog).ToContainTextAsync("Session paused");
        await Expect(dialog.Locator("#components-resume-button")).ToBeVisibleAsync();
        await Expect(dialog.Locator("#components-reconnect-button")).Not.ToBeVisibleAsync();

        await dialog.Locator("#components-resume-button").ClickAsync();

        await Expect(dialog).Not.ToBeVisibleAsync();
        await session.Page.WaitForFunctionAsync(
            "before => document.querySelector('[data-circuit]')?.getAttribute('data-circuit-instance') !== before", before);
        await session.WaitForCircuitAsync();
        await session.Page.ClickAsync("[data-action=server-start]");
        await Expect(session.Page.Locator("[data-lifecycle-message]")).ToContainTextAsync("Start enqueued");
        await session.AssertNoErrorsAsync();
    }
}
