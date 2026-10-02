using System.Text.RegularExpressions;
using Microsoft.Playwright;
using ZWarden.Domain.Ids;
using static Microsoft.Playwright.Assertions;

namespace ZWarden.Web.BrowserTests;

/// <summary>
/// The Server Detail smoke tier (#299, ADR 0046 Q4): each rail section opens in a real browser, does one
/// representative action, and the browser reports no error. Selectors are the pages' <c>data-*</c> hooks, so the same
/// tests hold while the page moves from static forms to circuit handlers.
/// </summary>
[ClassDataSource<BrowserHost>(Shared = SharedType.PerTestSession)]
public sealed partial class ServerDetailSmokeTests(BrowserHost host)
{
    [Test]
    public async Task Overview_starts_the_server()
    {
        await using BrowserSession session = await OpenSectionAsync("smoke-overview", section: null);

        await session.Page.ClickAsync("[data-action=server-start]");

        await Expect(session.Page.Locator("[data-lifecycle-message]")).ToContainTextAsync("Start enqueued");
        await session.AssertNoErrorsAsync();
    }

    [Test]
    public async Task Players_refreshes_the_roster()
    {
        await using BrowserSession session = await OpenSectionAsync("smoke-players", "players");

        await session.Page.ClickAsync("[data-action=refresh]");

        await Expect(session.Page.Locator("[data-player-message]")).ToContainTextAsync("Roster refresh started");
        await session.AssertNoErrorsAsync();
    }

    [Test]
    public async Task Console_runs_a_command()
    {
        await using BrowserSession session = await OpenSectionAsync("smoke-console", "console");

        await session.FillAsync("#console-input", "players");
        await session.Page.ClickAsync("[data-action=run-console]");

        await Expect(session.Page.Locator("[data-console-message]")).ToContainTextAsync("Command enqueued");
        await session.AssertNoErrorsAsync();
    }

    [Test]
    public async Task Logs_filters_the_live_tail()
    {
        await using BrowserSession session = await OpenSectionAsync("smoke-logs", "logs");

        await Expect(session.Page.Locator("[data-live-logs]")).ToBeVisibleAsync();
        await session.Page.GetByPlaceholder("Filter lines…").FillAsync("zombie");

        await Expect(session.Page.Locator("[data-log-empty]")).ToBeVisibleAsync();
        await session.AssertNoErrorsAsync();
    }

    [Test]
    public async Task Config_marks_an_edited_setting_as_changed()
    {
        await using BrowserSession session = await OpenSectionAsync("smoke-config", "config", "&file=SandboxVars");
        ILocator row = session.Page.Locator("[data-cfg-row]", new() { HasText = "Population" });

        await row.Locator("select[data-cfg-value]").SelectOptionAsync("1");

        await Expect(row).ToHaveClassAsync(ChangedRow());
        await session.AssertNoErrorsAsync();
    }

    [Test]
    public async Task Config_keeps_an_unsaved_edit_across_a_circuit_pause_and_resume()
    {
        // #299 D1: the unsaved edits are a [PersistentState] draft. Pausing evicts the circuit after persisting its
        // state, exactly as a dropped connection's eviction does; resuming builds a new circuit from that state.
        await using BrowserSession session = await OpenSectionAsync("smoke-draft", "config", "&file=SandboxVars");
        ILocator row = session.Page.Locator("[data-cfg-row]", new() { HasText = "Population" });
        await row.Locator("select[data-cfg-value]").SelectOptionAsync("1");
        await Expect(row).ToHaveClassAsync(ChangedRow());

        string before = await session.Page.GetAttributeAsync("[data-circuit]", "data-circuit-instance") ?? string.Empty;
        await Assert.That(await session.Page.EvaluateAsync<bool>("() => Blazor.pauseCircuit()")).IsTrue();
        await Assert.That(await session.Page.EvaluateAsync<bool>("() => Blazor.resumeCircuit()")).IsTrue();
        // The old markup stays on screen until the new circuit renders, so wait for a new page instance.
        await session.Page.WaitForFunctionAsync(
            "before => document.querySelector('[data-circuit]')?.getAttribute('data-circuit-instance') !== before", before);
        await session.WaitForCircuitAsync();

        await Expect(row).ToHaveClassAsync(ChangedRow());
        await Expect(row.Locator("select[data-cfg-value]")).ToHaveValueAsync("1");
        await session.AssertNoErrorsAsync();
    }

    [Test]
    public async Task The_rail_and_the_config_file_tabs_switch_in_place_without_a_reload()
    {
        // #299: the rail and the file tabs are ordinary links, so they stay bookmarkable; on the interactive page the
        // same circuit renders the new section (no full-page reload, no new page instance).
        await using BrowserSession session = await OpenSectionAsync("smoke-rail", section: null);
        string instance = await session.Page.GetAttributeAsync("[data-circuit]", "data-circuit-instance") ?? string.Empty;

        await session.Page.ClickAsync("[data-rail-item=players]");
        await Expect(session.Page.Locator("[data-players-card]")).ToBeVisibleAsync();
        await session.Page.ClickAsync("[data-rail-item=config]");
        await Expect(session.Page.Locator("[data-cfg-form]")).ToBeVisibleAsync();
        await session.Page.ClickAsync("[data-config-tab=SandboxVars]");
        await Expect(session.Page.Locator("[data-config-tab=SandboxVars]")).ToHaveClassAsync(ActiveTab());

        await Expect(session.Page).ToHaveURLAsync(SandboxVarsUrl());
        await Assert.That(await session.Page.GetAttributeAsync("[data-circuit]", "data-circuit-instance")).IsEqualTo(instance);
        await session.AssertNoErrorsAsync();
    }

    [Test]
    public async Task Mods_starts_a_discovery()
    {
        await using BrowserSession session = await OpenSectionAsync("smoke-mods", "mods");

        await session.Page.ClickAsync("[data-action=mod-refresh]");

        await Expect(session.Page.Locator("[data-mod-message]")).ToContainTextAsync("Discovery started");
        await session.AssertNoErrorsAsync();
    }

    [Test]
    public async Task Mod_browser_explains_an_unresolvable_reference()
    {
        await using BrowserSession session = await OpenSectionAsync("smoke-modbrowser", "modbrowser");

        await session.FillAsync("#modbrowser-input", "not a workshop item");
        await session.Page.ClickAsync("[data-action=modbrowser-resolve]");

        await Expect(session.Page.Locator("[data-modbrowser-unresolvable]")).ToBeVisibleAsync();
        await session.AssertNoErrorsAsync();
    }

    [Test]
    public async Task Backups_takes_a_backup()
    {
        await using BrowserSession session = await OpenSectionAsync("smoke-backups", "backups");

        await session.Page.ClickAsync("[data-action=backup-create]");

        await Expect(session.Page.Locator("[data-backup-message]")).ToContainTextAsync("Backup enqueued");
        await session.AssertNoErrorsAsync();
    }

    [Test]
    public async Task Diagnostics_starts_a_gather()
    {
        await using BrowserSession session = await OpenSectionAsync("smoke-diagnostics", "diagnostics");

        await session.Page.ClickAsync("[data-action=run-diagnostics]");

        await Expect(session.Page.Locator("[data-diagnostics-message]")).ToContainTextAsync("Diagnostics gather started");
        await session.AssertNoErrorsAsync();
    }

    // A fresh Server per test, so one test's in-flight Operation never makes another's Server busy.
    private async Task<BrowserSession> OpenSectionAsync(string serverName, string? section, string extraQuery = "")
    {
        ServerId serverId = await host.SeedServerAsync(serverName);
        string query = section is null ? string.Empty : $"?section={section}{extraQuery}";
        return await host.OpenAsync($"/servers/{serverId}{query}");
    }

    [GeneratedRegex(@"\bzw-cfg-changed\b")]
    private static partial Regex ChangedRow();

    [GeneratedRegex(@"\bactive\b")]
    private static partial Regex ActiveTab();

    [GeneratedRegex(@"\?section=config&file=SandboxVars$")]
    private static partial Regex SandboxVarsUrl();
}
