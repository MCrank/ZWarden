using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Configuration;
using ZWarden.Domain.Ids;
using ZWarden.Web.Components.Pages.Servers;

namespace ZWarden.Web.Tests.Servers.Sections;

/// <summary>
/// #312: a rail or file-tab click, and a back/forward between the page's entries, switch in the circuit
/// (in-place-nav.js hands the URL to the page) instead of an enhanced navigation, which made the server prerender the
/// whole page, including a live config read from the Agent, only to throw it away. The circuit's NavigationManager never
/// sees the switch; the page follows its own URL.
/// </summary>
public sealed class InPlaceNavigationTests
{
    [Test]
    public async Task The_page_takes_over_its_own_links_once_its_circuit_renders()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("inplace-attach");

        harness.Render(serverId);

        await Assert.That(harness.Context.JSInterop.Invocations["zwInPlaceNav.attach"].Single().Arguments[0])
            .IsEqualTo($"/servers/{serverId}");
    }

    [Test]
    public async Task An_in_place_move_switches_the_rail_section_without_a_navigation()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("inplace-rail");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId);
        string here = Uri(harness);

        bool shown = await cut.InvokeAsync(() => cut.Instance.NavigatedInPlace($"http://localhost/servers/{serverId}?section=logs"));

        await Assert.That(shown).IsTrue();
        cut.WaitForState(() => cut.Find("[data-section]").GetAttribute("data-section") == "logs");
        await Assert.That(cut.Find("[data-rail-item=logs]").ClassList).Contains("active");
        await Assert.That(Uri(harness)).IsEqualTo(here);
    }

    [Test]
    public async Task An_unauthorized_or_unknown_section_still_falls_back_to_the_overview()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("inplace-unknown");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "logs");

        await cut.InvokeAsync(() => cut.Instance.NavigatedInPlace($"http://localhost/servers/{serverId}?section=nope"));

        cut.WaitForState(() => cut.Find("[data-section]").GetAttribute("data-section") == "overview");
    }

    [Test]
    public async Task A_url_that_is_not_this_page_is_refused_and_changes_nothing()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("inplace-other");
        ServerId other = await harness.SeedServerAsync("inplace-other-2");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "logs");

        bool shown = await cut.InvokeAsync(() => cut.Instance.NavigatedInPlace($"http://localhost/servers/{other}?section=config"));

        await Assert.That(shown).IsFalse();
        await Assert.That(cut.Find("[data-section]").GetAttribute("data-section")).IsEqualTo("logs");
    }

    [Test]
    public async Task A_config_file_switch_reads_the_new_file_from_the_host_exactly_once()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Reader(out ConfigurationSectionTests.SwitchableReader reader));
        ServerId serverId = await harness.SeedServerAsync("inplace-file");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "config", "&file=SandboxVars");
        cut.WaitForState(() => cut.FindAll("[data-cfg-form]").Count == 1);
        int before = reader.Reads;

        await cut.InvokeAsync(() => cut.Instance.NavigatedInPlace($"http://localhost/servers/{serverId}?section=config&file=Ini"));

        cut.WaitForState(() => cut.Find("[data-config-tab=Ini]").ClassList.Contains("active"));
        await Assert.That(reader.Reads).IsEqualTo(before + 1);
    }

    [Test]
    public async Task Load_history_for_another_file_moves_in_place()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Reader(out _));
        ServerId serverId = await harness.SeedServerAsync("inplace-load");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "config", "&file=SandboxVars");
        string here = Uri(harness);

        await cut.Find("#config-file").ChangeAsync(new() { Value = "Ini" });
        await cut.Find("[data-action=config-load]").ClickAsync(new());

        cut.WaitForState(() => cut.Find("[data-config-tab=Ini]").ClassList.Contains("active"));
        await Assert.That(harness.Context.JSInterop.Invocations["zwInPlaceNav.push"].Single().Arguments[0])
            .IsEqualTo($"http://localhost/servers/{serverId}?section=config&file=Ini");
        await Assert.That(Uri(harness)).IsEqualTo(here);
    }

    [Test]
    public async Task A_real_navigation_on_the_page_still_switches_it()
    {
        // The fallback: a click before the circuit is up, or a link in-place-nav.js leaves alone, is an enhanced navigation.
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("inplace-fallback");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId);

        await cut.InvokeAsync(() => harness.Context.Services.GetRequiredService<NavigationManager>()
            .NavigateTo($"/servers/{serverId}?section=logs"));

        cut.WaitForState(() => cut.Find("[data-section]").GetAttribute("data-section") == "logs");
    }

    private static string Uri(InteractivePageHarness harness) =>
        harness.Context.Services.GetRequiredService<NavigationManager>().Uri;

    private static Action<IServiceCollection> Reader(out ConfigurationSectionTests.SwitchableReader reader)
    {
        var fake = new ConfigurationSectionTests.SwitchableReader(View());
        reader = fake;
        return s => s.AddSingleton<IServerConfigurationReader>(fake);
    }

    private static ConfigDocumentView View() => new(
        ConfigReadOutcome.Read,
        [
            new ConfigSection("Zombies",
            [
                new ConfigSettingView("Zombies", "Population", ConfigEditKind.Number, ConfigValueShape.Whole, "4",
                    1, 6, "4", "The zombie population.", [new ConfigOption("1", "Insane"), new ConfigOption("4", "Normal")],
                    KnownToSchema: true),
            ]),
        ],
        "VERSION = 1,\nZombies = 4,\n",
        "hash-abc",
        [],
        null);
}
