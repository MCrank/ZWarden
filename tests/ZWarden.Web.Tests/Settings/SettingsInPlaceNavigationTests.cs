using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using SettingsPage = ZWarden.Web.Components.Pages.Settings.Settings;

namespace ZWarden.Web.Tests.Settings;

/// <summary>
/// #344: a click on a Settings section, and a back/forward between Settings entries, switch in the circuit
/// (in-place-nav.js hands the URL to the page, #312) instead of an enhanced navigation that would prerender the page
/// again. The circuit's NavigationManager never sees the switch; the page follows its own URL.
/// </summary>
public sealed class SettingsInPlaceNavigationTests
{
    [Test]
    public async Task The_page_takes_over_its_section_links_once_its_circuit_renders()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();

        harness.RenderPage<SettingsPage>("/settings");

        IReadOnlyList<object?> arguments = harness.Context.JSInterop.Invocations["zwInPlaceNav.attach"].Single().Arguments;
        await Assert.That(arguments[0]).IsEqualTo("/settings");
        await Assert.That(arguments[3]?.ToString()).IsEqualTo("{ subpaths = True }"); // the sections below /settings too
    }

    [Test]
    public async Task An_in_place_move_switches_the_section_without_a_navigation()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        IRenderedComponent<SettingsPage> cut = harness.RenderPage<SettingsPage>("/settings");
        string here = Uri(harness);

        bool shown = await cut.InvokeAsync(() => cut.Instance.NavigatedInPlace("http://localhost/settings/about"));

        await Assert.That(shown).IsTrue();
        cut.WaitForState(() => cut.Find("[data-settings-section]").GetAttribute("data-settings-section") == "about");
        await Assert.That(cut.Find("[data-settings-nav-item=about]").ClassList).Contains("active");
        await Assert.That(cut.Find("[data-settings-nav-item=general]").ClassList).DoesNotContain("active");
        await Assert.That(Uri(harness)).IsEqualTo(here);
    }

    [Test]
    public async Task An_unknown_section_still_falls_back_to_general()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        IRenderedComponent<SettingsPage> cut = harness.RenderPage<SettingsPage>("/settings/security");

        await cut.InvokeAsync(() => cut.Instance.NavigatedInPlace("http://localhost/settings/nope"));

        cut.WaitForState(() => cut.Find("[data-settings-section]").GetAttribute("data-settings-section") == "general");
    }

    [Test]
    public async Task A_url_that_is_not_settings_is_refused_and_changes_nothing()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        IRenderedComponent<SettingsPage> cut = harness.RenderPage<SettingsPage>("/settings/security");

        bool shown = await cut.InvokeAsync(() => cut.Instance.NavigatedInPlace("http://localhost/hosts"));

        await Assert.That(shown).IsFalse();
        await Assert.That(cut.Find("[data-settings-section]").GetAttribute("data-settings-section")).IsEqualTo("security");
    }

    [Test]
    public async Task A_real_navigation_to_another_section_still_switches()
    {
        // The fallback before the circuit is up: an enhanced navigation the page follows through LocationChanged.
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        IRenderedComponent<SettingsPage> cut = harness.RenderPage<SettingsPage>("/settings");

        harness.Context.Services.GetRequiredService<NavigationManager>().NavigateTo("/settings/backups");

        cut.WaitForState(() => cut.Find("[data-settings-section]").GetAttribute("data-settings-section") == "backups");
    }

    private static string Uri(InteractivePageHarness harness) =>
        harness.Context.Services.GetRequiredService<NavigationManager>().Uri;
}
