using System.Runtime.CompilerServices;
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
        using (session.ExpectDisconnect())
        {
            await Assert.That(await session.Page.EvaluateAsync<bool>("() => Blazor.pauseCircuit()")).IsTrue();
            await Assert.That(await session.Page.EvaluateAsync<bool>("() => Blazor.resumeCircuit()")).IsTrue();
            // The old markup stays on screen until the new circuit renders, so wait for a new page instance.
            await session.Page.WaitForFunctionAsync(
                "before => document.querySelector('[data-circuit]')?.getAttribute('data-circuit-instance') !== before", before);
            await session.WaitForCircuitAsync();
        }

        await Expect(row).ToHaveClassAsync(ChangedRow());
        await Expect(row.Locator("select[data-cfg-value]")).ToHaveValueAsync("1");
        await session.AssertNoErrorsAsync();
    }

    [Test]
    public async Task The_rail_and_the_config_file_tabs_switch_in_place_without_a_reload()
    {
        // #299: the rail and the file tabs are ordinary links, so they stay bookmarkable; on the interactive page the
        // same circuit renders the new section (no full-page reload, no new page instance).
        // #322: the fixture is a real-sized SandboxVars, so its editor state is far past the hub's 32 KB receive limit;
        // were it posted to the circuit on a click, the hub would close the connection and a new one would open.
        // #312: the circuit switches by itself, so the server is never asked for the page again (no prerender), and a
        // file seen for the first time is read from the host exactly once.
        ServerId serverId = await host.SeedServerAsync("smoke-rail");
        await using BrowserSession session = await host.OpenAsync($"/servers/{serverId}");
        string instance = await session.Page.GetAttributeAsync("[data-circuit]", "data-circuit-instance") ?? string.Empty;
        int sockets = 0;
        session.Page.WebSocket += (_, _) => Interlocked.Increment(ref sockets);
        StrongBox<int> pageRequests = CountPageRequests(session, serverId);

        await session.Page.ClickAsync("[data-rail-item=players]");
        await Expect(session.Page.Locator("[data-players-card]")).ToBeVisibleAsync();
        await session.Page.ClickAsync("[data-rail-item=config]");
        await Expect(session.Page.Locator("[data-cfg-form]")).ToBeVisibleAsync();
        await session.WaitForCircuitAsync();
        await Assert.That(host.ConfigReads(serverId)).IsEqualTo(1);
        foreach (string tab in (string[])["SandboxVars", "SpawnRegions", "Ini", "SpawnPoints", "SandboxVars"])
        {
            int reads = host.ConfigReads(serverId);
            await session.Page.ClickAsync($"[data-config-tab={tab}]");
            await Expect(session.Page.Locator($"[data-config-tab={tab}]")).ToHaveClassAsync(ActiveTab());
            await session.WaitForCircuitAsync();
            await Assert.That(host.ConfigReads(serverId)).IsEqualTo(reads + 1).Because($"the switch to {tab} reads it once");
        }

        await Expect(session.Page).ToHaveURLAsync(SandboxVarsUrl());
        await Assert.That(await session.Page.GetAttributeAsync("[data-circuit]", "data-circuit-instance")).IsEqualTo(instance);
        await Assert.That(Volatile.Read(ref sockets)).IsEqualTo(0).Because("a click must not open a new circuit connection");
        await Assert.That(Volatile.Read(ref pageRequests.Value)).IsEqualTo(0).Because("a click must not ask the server for the page");
        await session.AssertNoErrorsAsync();
    }

    [Test]
    public async Task Back_and_forward_switch_in_place_and_a_switched_url_still_opens_its_section()
    {
        // #312: the address bar follows every switch, so back/forward walks the sections (still in the circuit, still no
        // request for the page) and the URL is a bookmark: loading it opens the same section and file.
        ServerId serverId = await host.SeedServerAsync("smoke-history");
        await using BrowserSession session = await host.OpenAsync($"/servers/{serverId}");
        StrongBox<int> pageRequests = CountPageRequests(session, serverId);

        await session.Page.ClickAsync("[data-rail-item=logs]");
        await Expect(session.Page.Locator("[data-live-logs]")).ToBeVisibleAsync();
        await session.Page.ClickAsync("[data-rail-item=config]");
        await session.Page.ClickAsync("[data-config-tab=SandboxVars]");
        await Expect(session.Page).ToHaveURLAsync(SandboxVarsUrl());
        await session.WaitForCircuitAsync();

        await session.Page.GoBackAsync();
        await Expect(session.Page).ToHaveURLAsync(ConfigUrl());
        await Expect(session.Page.Locator("[data-config-tab=Ini]")).ToHaveClassAsync(ActiveTab());
        await session.Page.GoBackAsync();
        await Expect(session.Page.Locator("[data-live-logs]")).ToBeVisibleAsync();
        await Expect(session.Page.Locator("[data-rail-item=logs]")).ToHaveClassAsync(ActiveTab());
        await session.Page.GoForwardAsync();
        await session.Page.GoForwardAsync();
        await Expect(session.Page).ToHaveURLAsync(SandboxVarsUrl());
        await Expect(session.Page.Locator("[data-config-tab=SandboxVars]")).ToHaveClassAsync(ActiveTab());
        await session.WaitForCircuitAsync();
        await Assert.That(Volatile.Read(ref pageRequests.Value)).IsEqualTo(0).Because("back/forward must not ask the server for the page");

        await session.Page.ReloadAsync();
        await session.WaitForCircuitAsync();
        await Expect(session.Page.Locator("[data-config-tab=SandboxVars]")).ToHaveClassAsync(ActiveTab());
        await session.AssertNoErrorsAsync();
    }

    [Test]
    public async Task Unsaved_config_edits_warn_before_a_reload_or_leaving_the_page()
    {
        // #331: the shell's links and a reload would drop the circuit's kept edits, so both ask first: a link in the app
        // with a Blueprint dialog, a reload with the browser's own prompt (the only one a browser allows there).
        await using BrowserSession session = await OpenSectionAsync("smoke-unsaved", "config", "&file=SandboxVars");
        ILocator row = session.Page.Locator("[data-cfg-row]", new() { HasText = "Population" });
        ILocator dialog = session.Page.Locator("[data-unsaved-dialog]");
        await row.Locator("select[data-cfg-value]").SelectOptionAsync("1");
        await Expect(row).ToHaveClassAsync(ChangedRow());
        TaskCompletionSource beforeUnload = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int nativeDialogs = 0;
        session.Page.Dialog += (_, native) =>
        {
            Interlocked.Increment(ref nativeDialogs);
            if (native.Type == "beforeunload")
            {
                beforeUnload.TrySetResult();
            }

            _ = native.DismissAsync();
        };

        // Leaving for the fleet list asks; Stay keeps the page and the edit.
        await session.Page.ClickAsync("[data-nav=fleet]");
        await Expect(dialog).ToBeVisibleAsync();
        await session.Page.ClickAsync("[data-action=unsaved-stay]");
        await Expect(dialog).ToBeHiddenAsync();
        await Expect(session.Page).ToHaveURLAsync(SandboxVarsUrl());
        await Expect(row).ToHaveClassAsync(ChangedRow());

        // Moving to another section and back never asks: the circuit keeps the edit.
        await session.Page.ClickAsync("[data-rail-item=logs]");
        await Expect(session.Page.Locator("[data-live-logs]")).ToBeVisibleAsync();
        await Expect(dialog).ToHaveCountAsync(0);
        await session.Page.ClickAsync("[data-rail-item=config]");
        await session.Page.ClickAsync("[data-config-tab=SandboxVars]");
        await Expect(row).ToHaveClassAsync(ChangedRow());
        await Assert.That(Volatile.Read(ref nativeDialogs)).IsEqualTo(0);

        // A reload gets the browser's own "Leave site?" prompt; dismissing it cancels the reload.
        try
        {
            await session.Page.ReloadAsync(new() { Timeout = 3000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            // The dismissed prompt cancels the reload, so it never completes.
        }

        await beforeUnload.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await Expect(row).ToHaveClassAsync(ChangedRow());

        // Discard and leave goes, with no second (browser) prompt on the way out.
        await session.Page.ClickAsync("[data-nav=fleet]");
        await session.Page.ClickAsync("[data-action=unsaved-leave]");
        await Expect(session.Page).ToHaveURLAsync(FleetUrl());
        await Assert.That(Volatile.Read(ref nativeDialogs)).IsEqualTo(1);
        await session.AssertNoErrorsAsync();
    }

    [Test]
    public async Task Mods_starts_a_discovery()
    {
        await using BrowserSession session = await OpenSectionAsync("smoke-mods", "mods");

        await session.Page.ClickAsync("[data-action=mod-refresh]");

        await Expect(session.Page.Locator("[data-mod-message]")).ToContainTextAsync("Re-scan started");
        await session.AssertNoErrorsAsync();
    }

    [Test]
    public async Task Add_mods_sheet_explains_input_that_is_not_a_workshop_link()
    {
        // #292: the Mod Browser merged into the Mods section's Add-mods sheet; its old link opens the sheet.
        await using BrowserSession session = await OpenSectionAsync("smoke-addmods", "modbrowser");

        await Expect(session.Page.Locator("[data-add-mods-sheet]")).ToBeVisibleAsync();
        await session.FillAsync("#add-mods-input", "not a workshop item");
        await session.Page.ClickAsync("[data-action=add-mods-find]");

        await Expect(session.Page.Locator("[data-add-mods-note]")).ToBeVisibleAsync();
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

    // Counts every request for the Server page itself (a document load, or an enhanced navigation's fetch) from now on.
    private static StrongBox<int> CountPageRequests(BrowserSession session, ServerId serverId)
    {
        StrongBox<int> count = new();
        string path = $"/servers/{serverId}";
        session.Page.Request += (_, request) =>
        {
            if (string.Equals(new Uri(request.Url).AbsolutePath.TrimEnd('/'), path, StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref count.Value);
            }
        };
        return count;
    }

    [GeneratedRegex(@"\bzw-cfg-changed\b")]
    private static partial Regex ChangedRow();

    [GeneratedRegex(@"\bactive\b")]
    private static partial Regex ActiveTab();

    [GeneratedRegex(@"\?section=config&file=SandboxVars$")]
    private static partial Regex SandboxVarsUrl();

    [GeneratedRegex(@"\?section=config$")]
    private static partial Regex ConfigUrl();

    [GeneratedRegex(@"/servers/?$")]
    private static partial Regex FleetUrl();
}
