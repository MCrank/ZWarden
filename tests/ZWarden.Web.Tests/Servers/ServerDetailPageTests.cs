using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Configuration;
using ZWarden.Application.Mods;
using ZWarden.Application.Servers;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Backups;
using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Identity;
using ZWarden.Infrastructure.Persistence;
using ZWarden.PzConfig.Revisions;
using ZWarden.Web.Tests.Account;

namespace ZWarden.Web.Tests.Servers;

/// <summary>
/// F19: the player-management surface on the <c>/servers/{id}</c> detail page. The Players card shows for an
/// operator holding the per-server Player.* permissions (fail-closed, gated in the page and re-checked in the
/// service), embeds the live roster island, and its static-SSR forms bind and enqueue non-mutating player
/// Operations end to end (no circuit). Exercised over the real host.
/// </summary>
public sealed class ServerDetailPageTests
{
    private const string StrongPassword = "correct horse battery staple";

    [Test]
    public async Task The_players_card_shows_the_actions_and_roster_for_a_permitted_operator()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "player-managed");

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}?section=players", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-players-card");
        await Assert.That(html).Contains("data-action=\"refresh\"");
        await Assert.That(html).Contains("data-action=\"kick\"");
        await Assert.That(html).Contains("data-action=\"ban\"");
        await Assert.That(html).Contains("data-action=\"unban\"");
        await Assert.That(html).Contains("data-action=\"remove-from-whitelist\"");
        // The live roster island prerendered with its awaiting state (no roster cached yet).
        await Assert.That(html).Contains("data-roster-awaiting");
        // The static-SSR form binds the username/reason by their model-path field names (BbInput auto-derives, #121).
        await Assert.That(html).Contains("name=\"_actionForm.Username\"");
        await Assert.That(html).Contains("name=\"_actionForm.Reason\"");
        await Assert.That(html).Contains("data-bans-empty");
        client.Dispose();
    }

    [Test]
    public async Task The_refresh_form_posts_and_enqueues_a_list_players_operation()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "enumerable");

        string page = await (await client.GetAsync(new Uri($"/servers/{serverId}?section=players", UriKind.Relative))).Content.ReadAsStringAsync();
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "player-action",
            ["_actionForm.Target"] = "refresh",
        };
        HttpResponseMessage post = await client.PostAsync(new Uri($"/servers/{serverId}?section=players", UriKind.Relative), new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        await Assert.That(EnqueuedKind(factory, serverId, OperationKind.ListPlayers)).IsTrue();
        client.Dispose();
    }

    [Test]
    public async Task The_kick_form_posts_and_enqueues_a_kick_carrying_the_username()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "kickable");

        string page = await (await client.GetAsync(new Uri($"/servers/{serverId}?section=players", UriKind.Relative))).Content.ReadAsStringAsync();
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "player-action",
            ["_actionForm.Target"] = "kick",
            ["_actionForm.Username"] = "Bob",
        };
        HttpResponseMessage post = await client.PostAsync(new Uri($"/servers/{serverId}?section=players", UriKind.Relative), new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Operation? op = db.Set<Operation>().FirstOrDefault(o => o.ServerId == serverId && o.Kind == OperationKind.KickPlayer);
        await Assert.That(op).IsNotNull();
        await Assert.That(op!.IsMutating).IsFalse();
        await Assert.That(op.CommandPayload).Contains("Bob");
        client.Dispose();
    }

    [Test]
    public async Task The_diagnostics_card_shows_the_export_button_for_a_permitted_operator()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "exportable");

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}?section=diagnostics", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-diagnostics-card");
        await Assert.That(html).Contains("data-action=\"export-support-package\"");
        // A native POST to the export endpoint so the browser downloads the returned ZIP.
        await Assert.That(html).Contains($"/api/servers/{serverId}/diagnostics/support-package");
        client.Dispose();
    }

    [Test]
    public async Task The_configuration_card_shows_the_edit_form_and_empty_history_for_a_permitted_operator()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "configurable");

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}?section=config", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-config-card");
        await Assert.That(html).Contains("data-action=\"config-apply\"");
        // The file select binds by its full model-path name under static SSR.
        await Assert.That(html).Contains("name=\"_configForm.File\"");
        await Assert.That(html).Contains("data-revisions-empty");
        client.Dispose();
    }

    [Test]
    public async Task The_apply_form_posts_and_enqueues_a_config_apply_carrying_the_edit()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "editable");

        string page = await (await client.GetAsync(new Uri($"/servers/{serverId}?section=config", UriKind.Relative))).Content.ReadAsStringAsync();
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "server-config",
            ["_configForm.File"] = "SandboxVars",
            ["_configForm.Path"] = "Zombies",
            ["_configForm.Kind"] = "Number",
            ["_configForm.Value"] = "1",
            ["_configForm.Target"] = "apply",
        };
        HttpResponseMessage post = await client.PostAsync(new Uri($"/servers/{serverId}?section=config", UriKind.Relative), new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Operation? op = db.Set<Operation>().FirstOrDefault(o => o.ServerId == serverId && o.Kind == OperationKind.ConfigApply);
        await Assert.That(op).IsNotNull();
        await Assert.That(op!.IsMutating).IsTrue();
        await Assert.That(op.CommandPayload).Contains("Zombies");
        client.Dispose();
    }

    [Test]
    public async Task The_history_table_lists_a_recorded_revision()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "with-history");
        await SeedRevisionAsync(factory, serverId, PzConfigFile.Ini, "[[\"PublicName\",\"s:First\"]]", DateTimeOffset.UtcNow);

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}?section=config", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-revisions-table");
        await Assert.That(html).Contains("data-revision-row");
        await Assert.That(html).DoesNotContain("data-revisions-empty");
        client.Dispose();
    }

    [Test]
    public async Task The_restore_button_posts_and_enqueues_a_config_apply()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "restorable");
        DateTimeOffset t0 = DateTimeOffset.UtcNow;
        ConfigurationRevisionId older = await SeedRevisionAsync(factory, serverId, PzConfigFile.Ini, "[[\"PublicName\",\"s:First\"]]", t0);
        await SeedRevisionAsync(factory, serverId, PzConfigFile.Ini, "[[\"PublicName\",\"s:Second\"]]", t0.AddMinutes(5));

        string page = await (await client.GetAsync(new Uri($"/servers/{serverId}?section=config", UriKind.Relative))).Content.ReadAsStringAsync();
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "config-restore",
            ["_restoreForm.Target"] = $"{PzConfigFile.Ini}|{older}",
        };
        HttpResponseMessage post = await client.PostAsync(new Uri($"/servers/{serverId}?section=config", UriKind.Relative), new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Operation? op = db.Set<Operation>().FirstOrDefault(o => o.ServerId == serverId && o.Kind == OperationKind.ConfigApply);
        await Assert.That(op).IsNotNull();
        await Assert.That(op!.CommandPayload).Contains("PublicName");
        client.Dispose();
    }

    [Test]
    public async Task The_configuration_editor_renders_grouped_prefilled_controls_from_a_live_read()
    {
        await using ZWardenWebAppFactory factory = new()
        {
            ConfigureTestServicesHook = static s =>
                s.AddSingleton<IServerConfigurationReader>(new FakeConfigReader(SampleView())),
        };
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "live-config");

        string html = await (await client.GetAsync(
            new Uri($"/servers/{serverId}?section=config&file=SandboxVars", UriKind.Relative))).Content.ReadAsStringAsync();

        // The live editor form, its file tabs and raw view render from the read.
        await Assert.That(html).Contains("data-cfg-form");
        await Assert.That(html).Contains("data-config-tabs");
        await Assert.That(html).Contains("data-config-raw");
        // A setting's hidden path + a boolean toggle + a schema label are rendered.
        await Assert.That(html).Contains("name=\"_editorForm.Rows[0].Path\"");
        await Assert.That(html).Contains("zw-cfg-switch");
        await Assert.That(html).Contains("Population");
        // A ranged-enum's option labels come from the comment (rendered as select options).
        await Assert.That(html).Contains(">Insane<");
        // An unknown/mod key is flagged as passed through unvalidated (the "Other" section).
        await Assert.That(html).Contains("Not in ZWarden's schema");
        // The by-path Advanced fallback is still available.
        await Assert.That(html).Contains("data-config-advanced");
        client.Dispose();
    }

    [Test]
    public async Task The_header_shows_an_in_flight_stop_and_disables_the_lifecycle_buttons()
    {
        // #249: the header is rendered from the observed state AND the in-flight lifecycle Operation, so the page
        // after a Stop already says STOPPING (the container reports Running for the whole stop grace), and it is
        // marked for live-status.js to keep current.
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "stopping");
        await client.PostAsync(new Uri($"/api/servers/{serverId}/stop", UriKind.Relative), content: null);

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains($"data-live-status=\"/api/servers/{serverId}/status\"");
        await Assert.That(html).Contains("data-status-busy=\"true\"");
        await Assert.That(html).Contains("STOPPING");
        await Assert.That(System.Text.RegularExpressions.Regex.IsMatch(
            html, "<button[^>]*data-action=\"server-start\"[^>]*>")).IsTrue();
        foreach (string action in new[] { "server-start", "server-stop", "server-restart" })
        {
            System.Text.RegularExpressions.Match button = System.Text.RegularExpressions.Regex.Match(
                html, $"<button[^>]*data-action=\"{action}\"[^>]*>");
            await Assert.That(button.Value).Contains("disabled");
        }

        client.Dispose();
    }

    [Test]
    public async Task The_header_shows_the_in_flight_operations_progress_as_text()
    {
        // #254: the restart countdown's status line sits beside RESTARTING. It is Agent-supplied, so it renders as
        // encoded text, never markup.
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "countdown");
        await client.PostAsync(new Uri($"/api/servers/{serverId}/restart", UriKind.Relative), content: null);
        await ServerLifecycleEndpointsTests.ReportProgressAsync(factory, serverId, "Restarting in 240 seconds <b>now</b>");

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-status-detail");
        await Assert.That(html).Contains("Restarting in 240 seconds &lt;b&gt;now&lt;/b&gt;");
        await Assert.That(html).DoesNotContain("<b>now</b>");
        client.Dispose();
    }

    [Test]
    public async Task The_configuration_editor_offers_collapse_and_expand_all_as_a_js_enhancement()
    {
        await using ZWardenWebAppFactory factory = new()
        {
            ConfigureTestServicesHook = static s =>
                s.AddSingleton<IServerConfigurationReader>(new FakeConfigReader(SampleView())),
        };
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "cfg-collapse");

        string html = await (await client.GetAsync(
            new Uri($"/servers/{serverId}?section=config&file=SandboxVars", UriKind.Relative))).Content.ReadAsStringAsync();

        // #243: both controls render in the toolbar, hidden until config-editor.js reveals them (they do nothing
        // without JS), and every section still renders open so the no-JS editor shows everything.
        await Assert.That(html).Contains("data-cfg-sections=\"collapse\"");
        await Assert.That(html).Contains("data-cfg-sections=\"expand\"");
        await Assert.That(html).Contains("data-cfg-sections-controls hidden");
        await Assert.That(html).DoesNotContain("<details class=\"zw-cfg-sec\" data-cfg-sec>");
        client.Dispose();
    }

    [Test]
    public async Task The_configuration_editor_applies_only_the_changed_settings()
    {
        await using ZWardenWebAppFactory factory = new()
        {
            ConfigureTestServicesHook = static s =>
                s.AddSingleton<IServerConfigurationReader>(new FakeConfigReader(SampleView())),
        };
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "live-config-apply");

        Uri url = new($"/servers/{serverId}?section=config&file=SandboxVars", UriKind.Relative);
        string page = await (await client.GetAsync(url)).Content.ReadAsStringAsync();
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "config-editor",
            ["_editorForm.File"] = "SandboxVars",
            // The baseline the operator was shown — matches the fresh read, so the apply proceeds (F20c, ADR 0042).
            ["_editorForm.BaselineHash"] = "hash-abc",
            ["_editorForm.Target"] = "apply",
            // Row 0 — PVP boolean, was true; its Flag is omitted (toggled off) so it must apply as false.
            ["_editorForm.Rows[0].Path"] = "PVP",
            ["_editorForm.Rows[0].Kind"] = "Bool",
            ["_editorForm.Rows[0].Toggle"] = "true",
            ["_editorForm.Rows[0].Original"] = "true",
            // Row 1 — PublicName unchanged.
            ["_editorForm.Rows[1].Path"] = "PublicName",
            ["_editorForm.Rows[1].Kind"] = "Text",
            ["_editorForm.Rows[1].Original"] = "My Server",
            ["_editorForm.Rows[1].Value"] = "My Server",
            // Row 2 — Zombies changed 4 → 2.
            ["_editorForm.Rows[2].Path"] = "Zombies",
            ["_editorForm.Rows[2].Kind"] = "Number",
            ["_editorForm.Rows[2].Original"] = "4",
            ["_editorForm.Rows[2].Value"] = "2",
            // Row 3 — the unknown key, unchanged.
            ["_editorForm.Rows[3].Path"] = "XpMultiplierGlobal",
            ["_editorForm.Rows[3].Kind"] = "Number",
            ["_editorForm.Rows[3].Original"] = "1.5",
            ["_editorForm.Rows[3].Value"] = "1.5",
        };
        HttpResponseMessage post = await client.PostAsync(url, new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        Operation? op = FirstOperation(factory, serverId, OperationKind.ConfigApply);
        await Assert.That(op).IsNotNull();
        await Assert.That(op!.IsMutating).IsTrue();
        // Only the two changed settings ride the apply — PVP toggled off (false) and Zombies.
        await Assert.That(op.CommandPayload).Contains("PVP");
        await Assert.That(op.CommandPayload).Contains("false");
        await Assert.That(op.CommandPayload).Contains("Zombies");
        await Assert.That(op.CommandPayload).DoesNotContain("PublicName");
        await Assert.That(op.CommandPayload).DoesNotContain("XpMultiplierGlobal");
        // The apply carries the operator's live-read baseline (not the last recorded revision) as the drift
        // baseline, so the Agent checks against exactly the state they saw (F20c, ADR 0042).
        await Assert.That(op.CommandPayload).Contains("hash-abc");
        client.Dispose();
    }

    [Test]
    public async Task The_configuration_editor_applies_a_full_size_sandbox_file_posted_without_script()
    {
        // #224: a real B42 SandboxVars has ~280 scalars; posted whole (the no-JS path) that is well over the form
        // reader's default 1024-value limit, which used to reject the POST with a bare 400 before the handler ran.
        await using ZWardenWebAppFactory factory = new()
        {
            ConfigureTestServicesHook = static s =>
                s.AddSingleton<IServerConfigurationReader>(new FakeConfigReader(LargeSandboxView(300))),
        };
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "large-sandbox");

        Uri url = new($"/servers/{serverId}?section=config&file=SandboxVars", UriKind.Relative);
        string page = await (await client.GetAsync(url)).Content.ReadAsStringAsync();
        List<KeyValuePair<string, string>> form =
        [
            new("__RequestVerificationToken", ParseHiddenInputs(page)["__RequestVerificationToken"]),
            new("_handler", "config-editor"),
            new("_editorForm.File", "SandboxVars"),
            new("_editorForm.BaselineHash", "hash-large"),
            new("_editorForm.Target", "apply"),
        ];
        for (int i = 0; i < 300; i++)
        {
            form.Add(new($"_editorForm.Rows[{i}].Path", $"Custom.Key{i}"));
            form.Add(new($"_editorForm.Rows[{i}].Kind", "Number"));
            form.Add(new($"_editorForm.Rows[{i}].Original", "1"));
            form.Add(new($"_editorForm.Rows[{i}].Value", i == 150 ? "2" : "1"));
        }

        HttpResponseMessage post = await client.PostAsync(url, new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        Operation? op = FirstOperation(factory, serverId, OperationKind.ConfigApply);
        await Assert.That(op).IsNotNull();
        await Assert.That(op!.CommandPayload).Contains("Custom.Key150");
        await Assert.That(op.CommandPayload).DoesNotContain("Custom.Key149");
        client.Dispose();
    }

    // A SandboxVars view with the given number of unknown numeric scalars (#224 full-size fixture).
    private static ConfigDocumentView LargeSandboxView(int count) => new(
        ConfigReadOutcome.Read,
        [
            new ConfigSection("Other",
            [
                .. Enumerable.Range(0, count).Select(i => new ConfigSettingView(
                    $"Custom.Key{i}", $"Custom.Key{i}", ConfigEditKind.Number, ConfigValueShape.Whole, "1",
                    null, null, null, null, [], KnownToSchema: false)),
            ]),
        ],
        "SandboxVars = {}\n",
        "hash-large",
        [],
        null);

    [Test]
    public async Task The_configuration_editor_applies_the_changed_rows_only_post_that_config_editor_js_sends()
    {
        // #224: with scripts on, config-editor.js disables untouched rows and renumbers the changed ones from 0, so
        // setting #150 of a 300-row file arrives as Rows[0]. The handler keys on each row's Path, not its index.
        await using ZWardenWebAppFactory factory = new()
        {
            ConfigureTestServicesHook = static s =>
                s.AddSingleton<IServerConfigurationReader>(new FakeConfigReader(LargeSandboxView(300))),
        };
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "trimmed-post");

        Uri url = new($"/servers/{serverId}?section=config&file=SandboxVars", UriKind.Relative);
        string page = await (await client.GetAsync(url)).Content.ReadAsStringAsync();
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "config-editor",
            ["_editorForm.File"] = "SandboxVars",
            ["_editorForm.BaselineHash"] = "hash-large",
            ["_editorForm.Target"] = "apply",
            ["_editorForm.Rows[0].Path"] = "Custom.Key150",
            ["_editorForm.Rows[0].Kind"] = "Number",
            ["_editorForm.Rows[0].Original"] = "1",
            ["_editorForm.Rows[0].Value"] = "2",
        };
        HttpResponseMessage post = await client.PostAsync(url, new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        Operation? op = FirstOperation(factory, serverId, OperationKind.ConfigApply);
        await Assert.That(op).IsNotNull();
        await Assert.That(op!.CommandPayload).Contains("Custom.Key150");
        client.Dispose();
    }

    [Test]
    public async Task An_oversized_form_post_redirects_back_with_an_operator_message_instead_of_a_bare_400()
    {
        // #224: a form the reader still refuses (past the page's raised limit) must not end on a bare browser 400 —
        // it redirects back to the same page, which explains that nothing was applied.
        await using ZWardenWebAppFactory factory = new()
        {
            ConfigureTestServicesHook = static s =>
                s.AddSingleton<IServerConfigurationReader>(new FakeConfigReader(SampleView())),
        };
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "oversized-post");

        Uri url = new($"/servers/{serverId}?section=config&file=SandboxVars", UriKind.Relative);
        string page = await (await client.GetAsync(url)).Content.ReadAsStringAsync();
        List<KeyValuePair<string, string>> form =
        [
            new("__RequestVerificationToken", ParseHiddenInputs(page)["__RequestVerificationToken"]),
            new("_handler", "config-editor"),
        ];
        form.AddRange(Enumerable.Range(0, 10_000).Select(i => new KeyValuePair<string, string>($"x{i}", "1")));

        HttpResponseMessage post = await client.PostAsync(url, new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsEqualTo(303);
        string location = post.Headers.Location!.OriginalString;
        await Assert.That(location).StartsWith($"/servers/{serverId}?section=config&file=SandboxVars");
        await Assert.That(location).Contains("formRejected=too-large");

        string html = await (await client.GetAsync(new Uri(location, UriKind.Relative))).Content.ReadAsStringAsync();
        await Assert.That(html).Contains("data-config-form-rejected");
        await Assert.That(html).Contains("nothing was applied");
        await Assert.That(FirstOperation(factory, serverId, OperationKind.ConfigApply)).IsNull();
        client.Dispose();
    }

    [Test]
    public async Task The_configuration_editor_offers_a_damaged_boolean_as_a_choice_and_only_toggles_post_flags()
    {
        // #223: a toggle row carries the hidden Toggle marker; a schema boolean already damaged to "" is a choice list
        // with the empty value still selected, never an Off toggle.
        await using ZWardenWebAppFactory factory = new()
        {
            ConfigureTestServicesHook = static s =>
                s.AddSingleton<IServerConfigurationReader>(new FakeConfigReader(IniView())),
        };
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "ini-render");

        string html = await (await client.GetAsync(
            new Uri($"/servers/{serverId}?section=config&file=Ini", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("name=\"_editorForm.Rows[0].Toggle\"");
        await Assert.That(html).DoesNotContain("name=\"_editorForm.Rows[1].Toggle\"");
        await Assert.That(html).Contains("<select class=\"zw-cfg-input\" name=\"_editorForm.Rows[1].Value\"");
        await Assert.That(html).Contains(">(empty)<");
        client.Dispose();
    }

    [Test]
    public async Task The_configuration_editor_shows_managed_ports_read_only_with_the_host_port_players_use()
    {
        // #228: the INI ports are ZWarden's; the file's 16261 is the container port, not what players dial on a
        // second server, so the row names the host port from the container's published binding.
        ConfigDocumentView view = new(
            ConfigReadOutcome.Read,
            [
                new ConfigSection("Details",
                [
                    new ConfigSettingView("DefaultPort", "Game port", ConfigEditKind.Number, ConfigValueShape.Whole, "16261",
                        0, 65535, "16261", null, [], KnownToSchema: true, Managed: true),
                    new ConfigSettingView("MaxPlayers", "Max players", ConfigEditKind.Number, ConfigValueShape.Whole, "16",
                        1, 254, "32", null, [], KnownToSchema: true),
                ]),
                new ConfigSection("RCON",
                [
                    new ConfigSettingView("RCONPort", "RCON port", ConfigEditKind.Number, ConfigValueShape.Whole, "27015",
                        0, 65535, "27015", null, [], KnownToSchema: true, Managed: true),
                ]),
            ],
            "DefaultPort=16261\nMaxPlayers=16\nRCONPort=27015\n", "hash-ini", [], null);
        await using ZWardenWebAppFactory factory = new()
        {
            ConfigureTestServicesHook = s => s.AddSingleton<IServerConfigurationReader>(new FakeConfigReader(view)),
        };
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "ports", gamePort: 16265, queryPort: 16266);

        string html = await (await client.GetAsync(
            new Uri($"/servers/{serverId}?section=config&file=Ini", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("name=\"_editorForm.Rows[0].Value\" value=\"16261\" readonly");
        await Assert.That(html).DoesNotContain("name=\"_editorForm.Rows[1].Value\" value=\"16\" readonly");
        await Assert.That(html).Contains("name=\"_editorForm.Rows[2].Value\" value=\"27015\" readonly");
        string text = System.Net.WebUtility.HtmlDecode(html);
        await Assert.That(text).Contains("Managed by ZWarden · players connect on host port 16265");
        await Assert.That(text).Contains("Managed by ZWarden · used by ZWarden's RCON connection, not published on the host");
        client.Dispose();
    }

    [Test]
    public async Task The_configuration_editor_leaves_an_untouched_damaged_boolean_alone_on_an_unrelated_apply()
    {
        // #223 recovery: applying an unrelated change must not write the damaged boolean (it used to post "" and,
        // once typed as a toggle, would have posted "false").
        await using ZWardenWebAppFactory factory = new()
        {
            ConfigureTestServicesHook = static s =>
                s.AddSingleton<IServerConfigurationReader>(new FakeConfigReader(IniView())),
        };
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "ini-unrelated");

        Uri url = new($"/servers/{serverId}?section=config&file=Ini", UriKind.Relative);
        string page = await (await client.GetAsync(url)).Content.ReadAsStringAsync();
        Dictionary<string, string> form = IniEditorForm(page);
        form["_editorForm.Rows[2].Value"] = "20";
        HttpResponseMessage post = await client.PostAsync(url, new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        Operation? op = FirstOperation(factory, serverId, OperationKind.ConfigApply);
        await Assert.That(op).IsNotNull();
        await Assert.That(op!.CommandPayload).Contains("MaxPlayers");
        await Assert.That(op.CommandPayload).DoesNotContain("PVP");
        await Assert.That(op.CommandPayload).DoesNotContain("Open");
        client.Dispose();
    }

    [Test]
    public async Task The_configuration_editor_surfaces_a_schema_rejection_and_applies_nothing()
    {
        await using ZWardenWebAppFactory factory = new()
        {
            ConfigureTestServicesHook = static s =>
                s.AddSingleton<IServerConfigurationReader>(new FakeConfigReader(IniView())),
        };
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "ini-reject");

        Uri url = new($"/servers/{serverId}?section=config&file=Ini", UriKind.Relative);
        string page = await (await client.GetAsync(url)).Content.ReadAsStringAsync();
        Dictionary<string, string> form = IniEditorForm(page);
        form["_editorForm.Rows[2].Value"] = "5000"; // MaxPlayers is 1..100.
        HttpResponseMessage post = await client.PostAsync(url, new FormUrlEncodedContent(form));
        string html = await post.Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-config-message");
        await Assert.That(html).Contains("MaxPlayers");
        await Assert.That(html).Contains("outside the allowed range");
        await Assert.That(FirstOperation(factory, serverId, OperationKind.ConfigApply)).IsNull();
        client.Dispose();
    }

    // The unchanged IniView form as the browser would post it: PVP toggle on, Open an untouched (empty) choice,
    // MaxPlayers unchanged.
    private static Dictionary<string, string> IniEditorForm(string page) => new(StringComparer.Ordinal)
    {
        ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
        ["_handler"] = "config-editor",
        ["_editorForm.File"] = "Ini",
        ["_editorForm.BaselineHash"] = "hash-ini",
        ["_editorForm.Target"] = "apply",
        ["_editorForm.Rows[0].Path"] = "PVP",
        ["_editorForm.Rows[0].Kind"] = "Bool",
        ["_editorForm.Rows[0].Original"] = "true",
        ["_editorForm.Rows[0].Toggle"] = "true",
        ["_editorForm.Rows[0].Flag"] = "true",
        ["_editorForm.Rows[1].Path"] = "Open",
        ["_editorForm.Rows[1].Kind"] = "Bool",
        ["_editorForm.Rows[1].Original"] = "",
        ["_editorForm.Rows[1].Value"] = "",
        ["_editorForm.Rows[2].Path"] = "MaxPlayers",
        ["_editorForm.Rows[2].Kind"] = "Number",
        ["_editorForm.Rows[2].Original"] = "16",
        ["_editorForm.Rows[2].Value"] = "16",
    };

    // What the reader produces for an INI with PVP=true, a damaged Open= and MaxPlayers=16 (#223).
    private static ConfigDocumentView IniView() => new(
        ConfigReadOutcome.Read,
        [
            new ConfigSection("Access",
            [
                new ConfigSettingView("PVP", "PVP", ConfigEditKind.Bool, ConfigValueShape.Boolean, "true",
                    null, null, "true", null, [], KnownToSchema: true),
                new ConfigSettingView("Open", "Open server", ConfigEditKind.Bool, ConfigValueShape.Boolean, "",
                    null, null, "true", null, [new ConfigOption("true", "On"), new ConfigOption("false", "Off")],
                    KnownToSchema: true),
                new ConfigSettingView("MaxPlayers", "Max players", ConfigEditKind.Number, ConfigValueShape.Whole, "16",
                    1, 100, "32", null, [], KnownToSchema: true),
            ]),
        ],
        "PVP=true\nOpen=\nMaxPlayers=16\n",
        "hash-ini",
        [],
        null);

    [Test]
    public async Task A_config_apply_redirects_to_the_section_tracking_the_write_and_withholds_the_stale_editor()
    {
        // #226: after enqueue the page Post/Redirect/Gets onto ?op=, and while the write is still running it shows
        // "Applying…" with a self-refresh instead of re-rendering the pre-write values.
        await using ZWardenWebAppFactory factory = new()
        {
            ConfigureTestServicesHook = static s =>
                s.AddSingleton<IServerConfigurationReader>(new FakeConfigReader(SampleView())),
        };
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "prg-apply");

        string location = await PostZombiesChangeAsync(client, serverId);

        await Assert.That(location).StartsWith($"/servers/{serverId}?section=config&file=SandboxVars&op=");
        Operation op = FirstOperation(factory, serverId, OperationKind.ConfigApply)!;
        await Assert.That(location).EndsWith(op.Id.ToString());

        string html = await (await client.GetAsync(new Uri(location, UriKind.Relative))).Content.ReadAsStringAsync();
        await Assert.That(html).Contains("data-config-applying");
        await Assert.That(html).Contains("data-cfg-refresh=\"2\"");
        await Assert.That(html).DoesNotContain("data-cfg-form");
        client.Dispose();
    }

    [Test]
    public async Task A_finished_config_write_shows_its_result_line_and_the_fresh_editor()
    {
        await using ZWardenWebAppFactory factory = new()
        {
            ConfigureTestServicesHook = static s =>
                s.AddSingleton<IServerConfigurationReader>(new FakeConfigReader(SampleView())),
        };
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "prg-done");
        string location = await PostZombiesChangeAsync(client, serverId);
        await FinishOperationAsync(factory, serverId, succeeded: true, "Applied and reloaded live on the running server.");

        string html = await (await client.GetAsync(new Uri(location, UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-config-applied");
        await Assert.That(html).Contains("Applied and reloaded live on the running server.");
        await Assert.That(html).DoesNotContain("data-cfg-refresh");
        await Assert.That(html).Contains("data-cfg-form");
        client.Dispose();
    }

    [Test]
    public async Task A_failed_config_write_shows_its_failure_reason_in_the_editor()
    {
        await using ZWardenWebAppFactory factory = new()
        {
            ConfigureTestServicesHook = static s =>
                s.AddSingleton<IServerConfigurationReader>(new FakeConfigReader(SampleView())),
        };
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "prg-failed");
        string location = await PostZombiesChangeAsync(client, serverId);
        await FinishOperationAsync(factory, serverId, succeeded: false, "The configuration on disk changed outside ZWarden.");

        string html = await (await client.GetAsync(new Uri(location, UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-config-apply-failed");
        await Assert.That(html).Contains("The configuration on disk changed outside ZWarden.");
        client.Dispose();
    }

    [Test]
    public async Task An_op_parameter_for_another_servers_operation_is_ignored()
    {
        await using ZWardenWebAppFactory factory = new()
        {
            ConfigureTestServicesHook = static s =>
                s.AddSingleton<IServerConfigurationReader>(new FakeConfigReader(SampleView())),
        };
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId first = await SeedServerAsync(factory, "op-owner");
        ServerId second = await SeedServerAsync(factory, "op-other");
        await PostZombiesChangeAsync(client, first);
        Operation op = FirstOperation(factory, first, OperationKind.ConfigApply)!;

        string html = await (await client.GetAsync(new Uri(
            $"/servers/{second}?section=config&file=SandboxVars&op={op.Id}", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).DoesNotContain("data-config-applying");
        await Assert.That(html).Contains("data-cfg-form");
        client.Dispose();
    }

    [Test]
    public async Task A_drift_refusal_keeps_the_typed_edits_on_the_fresh_rows()
    {
        // #226: the refused edit (Zombies 4 → 2) is carried onto the re-rendered row, whose Original is the current
        // host value, so it stays highlighted and a second Apply writes it — the typed edit is not thrown away.
        await using ZWardenWebAppFactory factory = new()
        {
            ConfigureTestServicesHook = static s =>
                s.AddSingleton<IServerConfigurationReader>(new FakeConfigReader(SampleView())),
        };
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "drift-keep");

        Uri url = new($"/servers/{serverId}?section=config&file=SandboxVars", UriKind.Relative);
        string page = await (await client.GetAsync(url)).Content.ReadAsStringAsync();
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "config-editor",
            ["_editorForm.File"] = "SandboxVars",
            ["_editorForm.BaselineHash"] = "stale-hash",
            ["_editorForm.Target"] = "apply",
            ["_editorForm.Rows[0].Path"] = "Zombies",
            ["_editorForm.Rows[0].Kind"] = "Number",
            ["_editorForm.Rows[0].Original"] = "4",
            ["_editorForm.Rows[0].Value"] = "2",
        };
        string html = await (await client.PostAsync(url, new FormUrlEncodedContent(form))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-config-drift");
        // Zombies is row 2 in SampleView: its Original is still the host value, and the carried value "2" is the
        // selected choice (appended, since SampleView offers only 1/4/6).
        await Assert.That(html).Contains("name=\"_editorForm.Rows[2].Original\" value=\"4\"");
        await Assert.That(html).Contains("<option value=\"2\" selected>2</option>");
        await Assert.That(FirstOperation(factory, serverId, OperationKind.ConfigApply)).IsNull();
        client.Dispose();
    }

    [Test]
    public async Task The_advanced_by_path_apply_drift_checks_against_the_live_read()
    {
        // #226: the by-path form carries the live read's baseline, not the last recorded revision's.
        await using ZWardenWebAppFactory factory = new()
        {
            ConfigureTestServicesHook = static s =>
                s.AddSingleton<IServerConfigurationReader>(new FakeConfigReader(SampleView())),
        };
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "advanced-live");

        Uri url = new($"/servers/{serverId}?section=config&file=SandboxVars", UriKind.Relative);
        string page = await (await client.GetAsync(url)).Content.ReadAsStringAsync();
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "server-config",
            ["_configForm.File"] = "SandboxVars",
            ["_configForm.Path"] = "Zombies",
            ["_configForm.Kind"] = "Number",
            ["_configForm.Value"] = "3",
            ["_configForm.Target"] = "apply",
        };
        HttpResponseMessage post = await client.PostAsync(url, new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        Operation? op = FirstOperation(factory, serverId, OperationKind.ConfigApply);
        await Assert.That(op).IsNotNull();
        await Assert.That(op!.CommandPayload).Contains("hash-abc");
        client.Dispose();
    }

    // Posts a Zombies 4 → 2 change through the editor (in sync with SampleView's baseline) and returns the
    // post-apply redirect target (#226).
    private static async Task<string> PostZombiesChangeAsync(HttpClient client, ServerId serverId)
    {
        Uri url = new($"/servers/{serverId}?section=config&file=SandboxVars", UriKind.Relative);
        string page = await (await client.GetAsync(url)).Content.ReadAsStringAsync();
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "config-editor",
            ["_editorForm.File"] = "SandboxVars",
            ["_editorForm.BaselineHash"] = "hash-abc",
            ["_editorForm.Target"] = "apply",
            ["_editorForm.Rows[0].Path"] = "Zombies",
            ["_editorForm.Rows[0].Kind"] = "Number",
            ["_editorForm.Rows[0].Original"] = "4",
            ["_editorForm.Rows[0].Value"] = "2",
        };
        HttpResponseMessage post = await client.PostAsync(url, new FormUrlEncodedContent(form));
        await Assert.That((int)post.StatusCode).IsBetween(300, 399);
        // Blazor redirects to an absolute URL on the same origin; the tests navigate by its path and query.
        Uri location = post.Headers.Location!;
        return location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString;
    }

    // Drives the Server's pending config write to a terminal state, as the Agent's completion would (#226).
    private static async Task FinishOperationAsync(ZWardenWebAppFactory factory, ServerId serverId, bool succeeded, string text)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Operation op = db.Set<Operation>().First(o => o.ServerId == serverId && o.Kind == OperationKind.ConfigApply);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        op.MarkDispatched(now.AddMinutes(5), now);
        if (succeeded)
        {
            op.ReportProgress(100, text, now.AddMinutes(5), now);
            op.Succeed(now);
        }
        else
        {
            op.Fail(text, now);
        }

        await db.SaveChangesAsync();
    }

    [Test]
    public async Task The_configuration_editor_refuses_and_shows_the_drift_banner_when_the_file_changed()
    {
        await using ZWardenWebAppFactory factory = new()
        {
            ConfigureTestServicesHook = static s =>
                s.AddSingleton<IServerConfigurationReader>(new FakeConfigReader(SampleView())),
        };
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "config-drift");

        Uri url = new($"/servers/{serverId}?section=config&file=SandboxVars", UriKind.Relative);
        string page = await (await client.GetAsync(url)).Content.ReadAsStringAsync();
        // Post a STALE baseline — the operator loaded when the file held a different value; the fresh read the
        // POST performs returns "hash-abc", so the pre-check sees drift and must refuse to enqueue.
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "config-editor",
            ["_editorForm.File"] = "SandboxVars",
            ["_editorForm.BaselineHash"] = "stale-hash-from-an-earlier-load",
            ["_editorForm.Target"] = "apply",
            ["_editorForm.Rows[0].Path"] = "PVP",
            ["_editorForm.Rows[0].Kind"] = "Bool",
            ["_editorForm.Rows[0].Toggle"] = "true",
            ["_editorForm.Rows[0].Original"] = "true",
            ["_editorForm.Rows[2].Path"] = "Zombies",
            ["_editorForm.Rows[2].Kind"] = "Number",
            ["_editorForm.Rows[2].Original"] = "4",
            ["_editorForm.Rows[2].Value"] = "2",
        };
        HttpResponseMessage post = await client.PostAsync(url, new FormUrlEncodedContent(form));
        string html = await post.Content.ReadAsStringAsync();

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        // The confirm-and-override banner is shown and the current values are re-rendered…
        await Assert.That(html).Contains("data-config-drift");
        await Assert.That(html).Contains("data-cfg-form");
        // …and nothing was enqueued — a drift refusal never writes (fail-closed, ADR 0011/0042).
        await Assert.That(FirstOperation(factory, serverId, OperationKind.ConfigApply)).IsNull();
        client.Dispose();
    }

    [Test]
    public async Task The_raw_edit_form_renders_prefilled_for_a_permitted_operator()
    {
        await using ZWardenWebAppFactory factory = new()
        {
            ConfigureTestServicesHook = static s =>
                s.AddSingleton<IServerConfigurationReader>(new FakeConfigReader(SampleView())),
        };
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "raw-render");

        string html = await (await client.GetAsync(
            new Uri($"/servers/{serverId}?section=config&file=SandboxVars", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-config-raweditform");
        await Assert.That(html).Contains("data-config-rawconfirm");
        // The textarea is pre-filled with the live raw text.
        await Assert.That(html).Contains("VERSION = 1,");
        client.Dispose();
    }

    [Test]
    public async Task A_raw_edit_without_the_acknowledgement_is_refused_and_enqueues_nothing()
    {
        await using ZWardenWebAppFactory factory = new()
        {
            ConfigureTestServicesHook = static s =>
                s.AddSingleton<IServerConfigurationReader>(new FakeConfigReader(SampleView())),
        };
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "raw-unconfirmed");

        Uri url = new($"/servers/{serverId}?section=config&file=SandboxVars", UriKind.Relative);
        string page = await (await client.GetAsync(url)).Content.ReadAsStringAsync();
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "config-raw-edit",
            ["_rawForm.File"] = "SandboxVars",
            ["_rawForm.BaselineHash"] = "hash-abc",
            ["_rawForm.Content"] = "SandboxVars = {\n    Zombies = 1,\n}\n",
            // No _rawForm.Confirmed — the acknowledgement is required.
        };
        HttpResponseMessage post = await client.PostAsync(url, new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        await Assert.That(FirstOperation(factory, serverId, OperationKind.ConfigApplyRaw)).IsNull();
        client.Dispose();
    }

    [Test]
    public async Task A_confirmed_in_sync_raw_edit_stages_and_enqueues_a_raw_apply()
    {
        await using ZWardenWebAppFactory factory = new()
        {
            ConfigureTestServicesHook = static s =>
            {
                s.AddSingleton<IServerConfigurationReader>(new FakeConfigReader(SampleView()));
                // A staging channel that reports the text reached the (fake) host, so the editor enqueues the apply.
                s.AddSingleton<IServerConfigRawEditChannel>(new FakeRawEditChannel());
            },
        };
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "raw-apply");

        Uri url = new($"/servers/{serverId}?section=config&file=SandboxVars", UriKind.Relative);
        string page = await (await client.GetAsync(url)).Content.ReadAsStringAsync();
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "config-raw-edit",
            ["_rawForm.File"] = "SandboxVars",
            ["_rawForm.BaselineHash"] = "hash-abc",
            ["_rawForm.Content"] = "SandboxVars = {\n    Zombies = 1,\n}\n",
            ["_rawForm.Confirmed"] = "true",
        };
        HttpResponseMessage post = await client.PostAsync(url, new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        Operation? op = FirstOperation(factory, serverId, OperationKind.ConfigApplyRaw);
        await Assert.That(op).IsNotNull();
        await Assert.That(op!.IsMutating).IsTrue();
        // The tiny command payload carries the file, the live-read baseline, and the staging correlation id.
        await Assert.That(op.CommandPayload).Contains("hash-abc");
        await Assert.That(op.CommandPayload).Contains("corr-fake");
        client.Dispose();
    }

    [Test]
    public async Task A_confirmed_raw_edit_against_a_stale_baseline_shows_the_drift_banner_and_enqueues_nothing()
    {
        await using ZWardenWebAppFactory factory = new()
        {
            ConfigureTestServicesHook = static s =>
            {
                s.AddSingleton<IServerConfigurationReader>(new FakeConfigReader(SampleView()));
                s.AddSingleton<IServerConfigRawEditChannel>(new FakeRawEditChannel());
            },
        };
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "raw-drift");

        Uri url = new($"/servers/{serverId}?section=config&file=SandboxVars", UriKind.Relative);
        string page = await (await client.GetAsync(url)).Content.ReadAsStringAsync();
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "config-raw-edit",
            ["_rawForm.File"] = "SandboxVars",
            ["_rawForm.BaselineHash"] = "stale-hash",
            ["_rawForm.Content"] = "SandboxVars = {\n    Zombies = 1,\n}\n",
            ["_rawForm.Confirmed"] = "true",
        };
        HttpResponseMessage post = await client.PostAsync(url, new FormUrlEncodedContent(form));
        string html = await post.Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-config-drift");
        await Assert.That(FirstOperation(factory, serverId, OperationKind.ConfigApplyRaw)).IsNull();
        client.Dispose();
    }

    private sealed class FakeRawEditChannel : IServerConfigRawEditChannel
    {
        public Task<ConfigRawEditStage> StageAsync(
            ServerId server, AgentId owningAgent, string rawText, CancellationToken cancellationToken = default) =>
            Task.FromResult(ConfigRawEditStage.Ok("corr-fake"));
    }

    [Test]
    public async Task The_configuration_editor_shows_the_offline_state_when_the_host_is_unavailable()
    {
        // No fake reader — the real coordinator has no connected Agent, so the read is AgentOffline.
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "config-offline");

        string html = await (await client.GetAsync(
            new Uri($"/servers/{serverId}?section=config", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-config-unavailable");
        await Assert.That(html).Contains("offline");
        // Without a live read the schema editor is withheld, but the by-path Advanced fallback remains usable.
        await Assert.That(html).DoesNotContain("data-cfg-form");
        await Assert.That(html).Contains("data-config-advanced");
        client.Dispose();
    }

    private static ConfigDocumentView SampleView() => new(
        ConfigReadOutcome.Read,
        [
            new ConfigSection("Access",
            [
                new ConfigSettingView("PVP", "PVP", ConfigEditKind.Bool, ConfigValueShape.Boolean, "true",
                    null, null, null, "Allow player-versus-player combat.", [], KnownToSchema: true),
                new ConfigSettingView("PublicName", "Public name", ConfigEditKind.Text, ConfigValueShape.Text,
                    "My Server", null, null, null, null, [], KnownToSchema: true),
            ]),
            new ConfigSection("Zombies",
            [
                new ConfigSettingView("Zombies", "Population", ConfigEditKind.Number, ConfigValueShape.Whole, "4",
                    1, 6, "4", "The zombie population.",
                    [new ConfigOption("1", "Insane"), new ConfigOption("4", "Normal"), new ConfigOption("6", "None")],
                    KnownToSchema: true),
            ]),
            new ConfigSection("Other",
            [
                new ConfigSettingView("XpMultiplierGlobal", "XpMultiplierGlobal", ConfigEditKind.Number,
                    ConfigValueShape.Fractional, "1.5", null, null, null, null, [], KnownToSchema: false),
            ]),
        ],
        "VERSION = 1,\nZombies = 4,\n",
        "hash-abc",
        [],
        null);

    private sealed class FakeConfigReader(ConfigDocumentView view) : IServerConfigurationReader
    {
        public Task<ConfigDocumentView> ReadAsync(
            UserId user, ServerId server, PzConfigFile file, CancellationToken cancellationToken = default) =>
            Task.FromResult(view);
    }

    private static async Task<ConfigurationRevisionId> SeedRevisionAsync(
        ZWardenWebAppFactory factory, ServerId server, PzConfigFile file, string canonicalText, DateTimeOffset at)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        PzValueSnapshot snapshot = PzValueSnapshot.Parse(canonicalText);
        ConfigurationRevision revision = ConfigurationRevision.Record(server, file, snapshot.CanonicalText, snapshot.Hash, at);
        db.Set<ConfigurationRevision>().Add(revision);
        await db.SaveChangesAsync();
        return revision.Id;
    }

    [Test]
    public async Task The_mods_card_shows_for_a_permitted_operator()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "mod-managed");

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}?section=mods", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-mods-card");
        await Assert.That(html).Contains("data-action=\"mod-refresh\"");
        // The live inventory island prerendered with its awaiting state (no inventory cached yet).
        await Assert.That(html).Contains("data-mods-awaiting");
        client.Dispose();
    }

    [Test]
    public async Task The_mod_refresh_form_posts_and_enqueues_a_discovery()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "discoverable");

        string page = await (await client.GetAsync(new Uri($"/servers/{serverId}?section=mods", UriKind.Relative))).Content.ReadAsStringAsync();
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "mod-discovery",
        };
        HttpResponseMessage post = await client.PostAsync(new Uri($"/servers/{serverId}?section=mods", UriKind.Relative), new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        await Assert.That(EnqueuedKind(factory, serverId, OperationKind.ModDiscovery)).IsTrue();
        client.Dispose();
    }

    [Test]
    public async Task The_mod_management_section_shows_awaiting_without_a_cached_inventory()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "manageable");

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}?section=mods", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-mod-manage");
        // No inventory observed yet, so the actionable controls are withheld until discovery runs.
        await Assert.That(html).Contains("data-mod-manage-awaiting");
        client.Dispose();
    }

    [Test]
    public async Task The_mod_management_controls_render_from_a_cached_inventory()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        (ServerId serverId, AgentId agent) = await SeedServerAndAgentAsync(factory, "with-inventory");
        SeedInventory(
            factory, serverId, agent,
            installed:
            [
                new InstalledWorkshopItem("100", [new InstalledMod("ModA", null)]),
                new InstalledWorkshopItem("200", [new InstalledMod("ModB", null)]),
            ],
            workshop: ["100", "200"],
            enabled: ["ModA"]);

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}?section=mods", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).DoesNotContain("data-mod-manage-awaiting");
        await Assert.That(html).Contains("data-action=\"mod-add\"");
        await Assert.That(html).Contains("name=\"_modManageForm.WorkshopId\"");
        await Assert.That(html).Contains("data-mod-enabled-row");
        await Assert.That(html).Contains("data-action=\"mod-disable\"");
        // ModB is installed but not enabled, so it is offered as an enable candidate.
        await Assert.That(html).Contains("data-mod-enable");
        await Assert.That(html).Contains("data-mod-workshop-row");
        await Assert.That(html).Contains("data-action=\"mod-remove\"");
        await Assert.That(html).Contains("data-action=\"mod-update\"");
        await Assert.That(html).Contains("data-action=\"mod-restart\"");
        client.Dispose();
    }

    [Test]
    public async Task The_add_workshop_form_posts_and_enqueues_a_config_apply_touching_workshop_items()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        (ServerId serverId, AgentId agent) = await SeedServerAndAgentAsync(factory, "addable");
        SeedInventory(factory, serverId, agent, installed: [], workshop: ["100"], enabled: []);

        string page = await (await client.GetAsync(new Uri($"/servers/{serverId}?section=mods", UriKind.Relative))).Content.ReadAsStringAsync();
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "mod-manage",
            ["_modManageForm.WorkshopId"] = "200",
            ["_modManageForm.Command"] = "add",
        };
        HttpResponseMessage post = await client.PostAsync(new Uri($"/servers/{serverId}?section=mods", UriKind.Relative), new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        Operation? op = FirstOperation(factory, serverId, OperationKind.ConfigApply);
        await Assert.That(op).IsNotNull();
        await Assert.That(op!.IsMutating).IsTrue();
        await Assert.That(op.CommandPayload).Contains("WorkshopItems");
        await Assert.That(op.CommandPayload).Contains("200");
        client.Dispose();
    }

    [Test]
    public async Task The_enable_form_posts_and_enqueues_a_config_apply_touching_the_mods_list()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        (ServerId serverId, AgentId agent) = await SeedServerAndAgentAsync(factory, "enableable");
        SeedInventory(
            factory, serverId, agent,
            installed: [new InstalledWorkshopItem("100", [new InstalledMod("ModB", null)])],
            workshop: ["100"],
            enabled: []);

        string page = await (await client.GetAsync(new Uri($"/servers/{serverId}?section=mods", UriKind.Relative))).Content.ReadAsStringAsync();
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "mod-manage",
            ["_modManageForm.EnableModId"] = "ModB",
            ["_modManageForm.Command"] = "enable",
        };
        HttpResponseMessage post = await client.PostAsync(new Uri($"/servers/{serverId}?section=mods", UriKind.Relative), new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        Operation? op = FirstOperation(factory, serverId, OperationKind.ConfigApply);
        await Assert.That(op).IsNotNull();
        await Assert.That(op!.CommandPayload).Contains("Mods");
        await Assert.That(op.CommandPayload).Contains("ModB");
        client.Dispose();
    }

    [Test]
    public async Task The_disable_button_posts_and_enqueues_a_config_apply()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        (ServerId serverId, AgentId agent) = await SeedServerAndAgentAsync(factory, "disableable");
        SeedInventory(
            factory, serverId, agent,
            installed: [new InstalledWorkshopItem("100", [new InstalledMod("ModA", null)])],
            workshop: ["100"],
            enabled: ["ModA", "ModB"]);

        string page = await (await client.GetAsync(new Uri($"/servers/{serverId}?section=mods", UriKind.Relative))).Content.ReadAsStringAsync();
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "mod-manage",
            ["_modManageForm.Command"] = "disable|ModB",
        };
        HttpResponseMessage post = await client.PostAsync(new Uri($"/servers/{serverId}?section=mods", UriKind.Relative), new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        Operation? op = FirstOperation(factory, serverId, OperationKind.ConfigApply);
        await Assert.That(op).IsNotNull();
        await Assert.That(op!.CommandPayload).Contains("Mods");
        client.Dispose();
    }

    [Test]
    public async Task The_update_button_posts_and_enqueues_an_update_server_operation()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        (ServerId serverId, AgentId agent) = await SeedServerAndAgentAsync(factory, "updatable");
        SeedInventory(factory, serverId, agent, installed: [], workshop: ["100"], enabled: []);

        string page = await (await client.GetAsync(new Uri($"/servers/{serverId}?section=mods", UriKind.Relative))).Content.ReadAsStringAsync();
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "mod-manage",
            ["_modManageForm.Command"] = "update",
        };
        HttpResponseMessage post = await client.PostAsync(new Uri($"/servers/{serverId}?section=mods", UriKind.Relative), new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        await Assert.That(EnqueuedKind(factory, serverId, OperationKind.UpdateServer)).IsTrue();
        client.Dispose();
    }

    [Test]
    public async Task The_restart_button_posts_and_enqueues_a_restart_server_operation()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        (ServerId serverId, AgentId agent) = await SeedServerAndAgentAsync(factory, "restartable-mods");
        SeedInventory(factory, serverId, agent, installed: [], workshop: ["100"], enabled: []);

        string page = await (await client.GetAsync(new Uri($"/servers/{serverId}?section=mods", UriKind.Relative))).Content.ReadAsStringAsync();
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "mod-manage",
            ["_modManageForm.Command"] = "restart",
        };
        HttpResponseMessage post = await client.PostAsync(new Uri($"/servers/{serverId}?section=mods", UriKind.Relative), new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        await Assert.That(EnqueuedKind(factory, serverId, OperationKind.RestartServer)).IsTrue();
        client.Dispose();
    }

    [Test]
    public async Task The_graceful_restart_control_shows_for_a_permitted_operator()
    {
        // #114/#213: the always-visible restart panel lets the operator pick a countdown and warn players.
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "gracefully-restartable");

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-graceful-restart");
        await Assert.That(html).Contains("data-action=\"graceful-restart\"");
        await Assert.That(html).Contains("name=\"_gracefulForm.Message\"");
        // #213: the adjustable countdown preset selector is present.
        await Assert.That(html).Contains("name=\"_gracefulForm.Countdown\"");
        client.Dispose();
    }

    [Test]
    public async Task The_graceful_restart_immediate_preset_enqueues_an_empty_countdown()
    {
        // #213: choosing "Immediately" restarts with no warning broadcast (an empty countdown schedule).
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "graceful-immediate");

        string page = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "server-graceful-restart",
            ["_gracefulForm.Countdown"] = "immediate",
        };
        HttpResponseMessage post = await client.PostAsync(new Uri($"/servers/{serverId}", UriKind.Relative), new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        await Assert.That(EnqueuedKind(factory, serverId, OperationKind.RestartServer)).IsTrue();
        string? payload = EnqueuedPayload(factory, serverId, OperationKind.RestartServer);
        await Assert.That(payload).IsNotNull();
        await Assert.That(payload!).Contains("\"warningLeadSeconds\":[]");
        await Assert.That(payload!).DoesNotContain("300");
        client.Dispose();
    }

    [Test]
    public async Task The_graceful_restart_form_posts_and_enqueues_a_restart_carrying_the_plan()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "graceful-enqueue");

        string page = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "server-graceful-restart",
            ["_gracefulForm.Message"] = "Scheduled maintenance.",
        };
        HttpResponseMessage post = await client.PostAsync(new Uri($"/servers/{serverId}", UriKind.Relative), new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        await Assert.That(EnqueuedKind(factory, serverId, OperationKind.RestartServer)).IsTrue();
        string? payload = EnqueuedPayload(factory, serverId, OperationKind.RestartServer);
        await Assert.That(payload).IsNotNull();
        await Assert.That(payload!).Contains("Scheduled maintenance.");
        await Assert.That(payload!).Contains("300");
        client.Dispose();
    }

    [Test]
    public async Task The_logs_card_shows_and_prerenders_the_live_island_for_a_permitted_operator()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "log-viewable");

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}?section=logs", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-logs-card");
        await Assert.That(html).Contains("data-live-logs");
        // The interactive island prerenders its waiting state (no lines buffered yet).
        await Assert.That(html).Contains("data-log-empty");
        client.Dispose();
    }

    [Test]
    public async Task The_console_card_shows_and_prerenders_the_output_island_for_a_permitted_operator()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "console-runnable");

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}?section=console", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-console-card");
        await Assert.That(html).Contains("data-action=\"run-console\"");
        // The command input binds by its full model-path name under static SSR (BbInput auto-derives, #121).
        await Assert.That(html).Contains("name=\"_consoleForm.Input\"");
        // The interactive output island prerenders its empty state (no output cached yet).
        await Assert.That(html).Contains("data-console-empty");
        client.Dispose();
    }

    [Test]
    public async Task The_console_form_posts_and_enqueues_a_non_mutating_command_carrying_the_line()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "console-enqueue");

        string page = await (await client.GetAsync(new Uri($"/servers/{serverId}?section=console", UriKind.Relative))).Content.ReadAsStringAsync();
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "console-command",
            ["_consoleForm.Input"] = "servermsg \"hello\"",
        };
        HttpResponseMessage post = await client.PostAsync(new Uri($"/servers/{serverId}?section=console", UriKind.Relative), new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Operation? op = db.Set<Operation>().FirstOrDefault(o => o.ServerId == serverId && o.Kind == OperationKind.ExecuteConsoleCommand);
        await Assert.That(op).IsNotNull();
        await Assert.That(op!.IsMutating).IsFalse();
        await Assert.That(op.CommandPayload).Contains("servermsg");
        client.Dispose();
    }

    [Test]
    public async Task The_console_form_refuses_a_policy_blocked_command_without_enqueuing()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "console-denied");

        string page = await (await client.GetAsync(new Uri($"/servers/{serverId}?section=console", UriKind.Relative))).Content.ReadAsStringAsync();
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "console-command",
            ["_consoleForm.Input"] = "setpassword \"bob\" \"pw\"",
        };
        HttpResponseMessage post = await client.PostAsync(new Uri($"/servers/{serverId}?section=console", UriKind.Relative), new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Operation? op = db.Set<Operation>().FirstOrDefault(o => o.ServerId == serverId && o.Kind == OperationKind.ExecuteConsoleCommand);
        await Assert.That(op).IsNull();
        client.Dispose();
    }

    [Test]
    public async Task The_backups_card_shows_for_a_permitted_operator()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "backup-viewable");

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}?section=backups", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-backups-card");
        await Assert.That(html).Contains("data-action=\"backup-create\"");
        await Assert.That(html).Contains("data-backups-empty");
        client.Dispose();
    }

    [Test]
    public async Task The_take_backup_form_posts_and_enqueues_a_mutating_backup_operation()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "backup-takeable");

        string page = await (await client.GetAsync(new Uri($"/servers/{serverId}?section=backups", UriKind.Relative))).Content.ReadAsStringAsync();
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "backup-manage",
            ["_backupForm.Command"] = "create",
        };
        HttpResponseMessage post = await client.PostAsync(new Uri($"/servers/{serverId}?section=backups", UriKind.Relative), new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        Operation? op = FirstOperation(factory, serverId, OperationKind.Backup);
        await Assert.That(op).IsNotNull();
        await Assert.That(op!.IsMutating).IsTrue();
        await Assert.That(op.CommandPayload).Contains("Manual");
        client.Dispose();
    }

    [Test]
    public async Task The_delete_button_posts_and_enqueues_a_non_mutating_delete_backup_operation()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        (ServerId serverId, AgentId agent) = await SeedServerAndAgentAsync(factory, "backup-deletable");
        BackupId backupId = await SeedBackupAsync(factory, serverId, agent, "world-1.tar.gz");

        string page = await (await client.GetAsync(new Uri($"/servers/{serverId}?section=backups", UriKind.Relative))).Content.ReadAsStringAsync();
        await Assert.That(page).Contains("data-backup-row");
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "backup-manage",
            ["_backupForm.Command"] = $"delete|{backupId}",
        };
        HttpResponseMessage post = await client.PostAsync(new Uri($"/servers/{serverId}?section=backups", UriKind.Relative), new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        Operation? op = FirstOperation(factory, serverId, OperationKind.DeleteBackup);
        await Assert.That(op).IsNotNull();
        await Assert.That(op!.IsMutating).IsFalse();
        await Assert.That(op.CommandPayload).Contains("world-1.tar.gz");
        client.Dispose();
    }

    [Test]
    public async Task The_restore_button_posts_and_enqueues_a_mutating_restore_operation()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        (ServerId serverId, AgentId agent) = await SeedServerAndAgentAsync(factory, "backup-restorable");
        BackupId backupId = await SeedBackupAsync(factory, serverId, agent, "world-1.tar.gz");

        string page = await (await client.GetAsync(new Uri($"/servers/{serverId}?section=backups", UriKind.Relative))).Content.ReadAsStringAsync();
        await Assert.That(page).Contains("data-action=\"backup-restore\"");
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "backup-manage",
            ["_backupForm.Command"] = $"restore|{backupId}",
        };
        HttpResponseMessage post = await client.PostAsync(new Uri($"/servers/{serverId}?section=backups", UriKind.Relative), new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        Operation? op = FirstOperation(factory, serverId, OperationKind.Restore);
        await Assert.That(op).IsNotNull();
        await Assert.That(op!.IsMutating).IsTrue();
        await Assert.That(op.CommandPayload).Contains("world-1.tar.gz");
        await Assert.That(op.CommandPayload).Contains("abc123");
        client.Dispose();
    }

    private static async Task<BackupId> SeedBackupAsync(
        ZWardenWebAppFactory factory, ServerId server, AgentId agent, string archiveName)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Backup backup = Backup.Record(server, agent, archiveName, 2048, "abc123", BackupReason.Manual, DateTimeOffset.UtcNow);
        db.Set<Backup>().Add(backup);
        await db.SaveChangesAsync();
        return backup.Id;
    }

    private static bool EnqueuedKind(ZWardenWebAppFactory factory, ServerId serverId, OperationKind kind)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        return db.Set<Operation>().Any(o => o.ServerId == serverId && o.Kind == kind);
    }

    private static string? EnqueuedPayload(ZWardenWebAppFactory factory, ServerId serverId, OperationKind kind)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        return db.Set<Operation>()
            .Where(o => o.ServerId == serverId && o.Kind == kind)
            .Select(o => o.CommandPayload)
            .FirstOrDefault();
    }

    private static Operation? FirstOperation(ZWardenWebAppFactory factory, ServerId serverId, OperationKind kind)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        return db.Set<Operation>().FirstOrDefault(o => o.ServerId == serverId && o.Kind == kind);
    }

    private static void SeedInventory(
        ZWardenWebAppFactory factory,
        ServerId server,
        AgentId agent,
        IReadOnlyList<InstalledWorkshopItem> installed,
        IReadOnlyList<string> workshop,
        IReadOnlyList<string> enabled)
    {
        IModInventoryCache cache = factory.Services.GetRequiredService<IModInventoryCache>();
        cache.Record(new ModInventory(server, agent, installed, workshop, enabled, [], DateTimeOffset.UtcNow));
    }

    private static async Task<(ServerId Server, AgentId Agent)> SeedServerAndAgentAsync(ZWardenWebAppFactory factory, string name)
    {
        AgentId agent = AgentId.New();
        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Server server = Server.Import(agent, ServerId.New(), name, DateTimeOffset.UtcNow);
        db.Set<Server>().Add(server);
        await db.SaveChangesAsync();
        return (server.Id, agent);
    }

    [Test]
    public async Task The_last_failed_action_and_its_reason_show_under_the_header()
    {
        // #266: a refused Recreate is not a silent no-op — the page says what failed and why.
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "refused", gamePort: 17000, queryPort: 17001);
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
            Operation op = Operation.Enqueue(AgentId.New(), OperationKind.RecreateServer, isMutating: true, "k", DateTimeOffset.UtcNow, serverId);
            op.MarkDispatched(DateTimeOffset.UtcNow.AddMinutes(5), DateTimeOffset.UtcNow);
            op.Fail("Host port 16261/udp is already published by another container on this host.", DateTimeOffset.UtcNow);
            db.Add(op);
            await db.SaveChangesAsync();
        }

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-last-failure-reason>Host port 16261/udp is already published");
        await Assert.That(html).Contains("data-last-failure-action>Recreate</span>");
        await Assert.That(html).Contains("data-last-failure-dismiss");
        client.Dispose();
    }

    [Test]
    public async Task No_failure_alert_is_visible_when_nothing_has_failed()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "fine");

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();

        // Rendered (so the live script can fill it in) but hidden.
        await Assert.That(Regex.IsMatch(html, "<div[^>]*data-last-failure[^>]*hidden")).IsTrue();
        client.Dispose();
    }

    [Test]
    public async Task The_change_ports_control_shows_the_current_pair_for_a_permitted_owner()
    {
        // #229: an owner holds Server.Recreate, so the overview offers the data-preserving recreate on a new pair.
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "movable", gamePort: 16261, queryPort: 16262);

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-recreate");
        await Assert.That(html).Contains("name=\"_recreateForm.GamePort\"");
        await Assert.That(html).Contains("name=\"_recreateForm.Countdown\"");
        await Assert.That(html).Contains("data-action=\"recreate\"");
        client.Dispose();
    }

    [Test]
    public async Task The_change_ports_form_enqueues_a_recreate_carrying_the_port_and_countdown()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "move-me", gamePort: 16261, queryPort: 16262);

        string page = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "server-recreate",
            ["_recreateForm.GamePort"] = "27015",
            ["_recreateForm.Countdown"] = "1m",
        };
        HttpResponseMessage post = await client.PostAsync(new Uri($"/servers/{serverId}", UriKind.Relative), new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        string? payload = EnqueuedPayload(factory, serverId, OperationKind.RecreateServer);
        await Assert.That(payload).IsNotNull();
        ServerContainerPayload parsed = ServerContainerPayload.FromJson(payload!);
        await Assert.That(parsed.GamePort).IsEqualTo(27015);
        await Assert.That(parsed.Plan!.WarningLeadSeconds).IsEquivalentTo([60, 30, 10]);
        client.Dispose();
    }

    [Test]
    public async Task The_container_settings_form_recreates_with_a_new_heap()
    {
        // #230: the recreate form also changes the heap (typed in GiB; blank keeps the current heap).
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "grow-me", gamePort: 16261, queryPort: 16262);

        string page = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();
        await Assert.That(page).Contains("name=\"_recreateForm.HeapGiB\"");
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "server-recreate",
            ["_recreateForm.HeapGiB"] = "8",
            ["_recreateForm.Countdown"] = "5m",
        };
        await client.PostAsync(new Uri($"/servers/{serverId}", UriKind.Relative), new FormUrlEncodedContent(form));

        ServerContainerPayload parsed = ServerContainerPayload.FromJson(EnqueuedPayload(factory, serverId, OperationKind.RecreateServer)!);
        await Assert.That(parsed.HeapSizeBytes).IsEqualTo(8L * 1024 * 1024 * 1024);
        await Assert.That(parsed.GamePort).IsNull();
        client.Dispose();
    }

    [Test]
    public async Task The_change_ports_form_refuses_an_invalid_port_without_enqueueing()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "bad-move", gamePort: 16261, queryPort: 16262);

        string page = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "server-recreate",
            ["_recreateForm.GamePort"] = "80",
        };
        HttpResponseMessage post = await client.PostAsync(new Uri($"/servers/{serverId}", UriKind.Relative), new FormUrlEncodedContent(form));
        string html = await post.Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-lifecycle-message");
        await Assert.That(html).Contains("between 1024 and 65534");
        await Assert.That(EnqueuedKind(factory, serverId, OperationKind.RecreateServer)).IsFalse();
        client.Dispose();
    }

    [Test]
    public async Task An_operator_role_without_server_recreate_does_not_see_the_change_ports_control()
    {
        // D2 (#229): Operator runs the server day to day but does not re-provision it.
        await using ZWardenWebAppFactory factory = new();
        await factory.CreateConfirmedUserAsync("owner@zwarden.test", StrongPassword);
        await AuthorizationBootstrapper.EnsureSeededAsync(factory.Services, "owner@zwarden.test");
        await factory.CreateConfirmedUserAsync("operator@zwarden.test", StrongPassword);
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
            ApplicationUser user = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
                .FindByEmailAsync("operator@zwarden.test"))!;
            Role operatorRole = await db.Set<Role>().SingleAsync(r => r.BuiltIn == BuiltInRoleKind.Operator);
            db.Set<RoleAssignment>().Add(RoleAssignment.TenantWide(operatorRole.TenantId, UserId.FromGuid(user.Id), operatorRole.Id));
            await db.SaveChangesAsync();
        }

        ServerId serverId = await SeedServerAsync(factory, "operated", gamePort: 16261, queryPort: 16262);
        HttpClient client = factory.CreateWebClient();
        await LoginAsync(client, "operator@zwarden.test", StrongPassword);

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-graceful-restart"); // the operator does see the restart panel …
        await Assert.That(html).DoesNotContain("data-recreate");   // … but not the recreate control.
        client.Dispose();
    }
    private static async Task<HttpClient> SignedInOperatorAsync(ZWardenWebAppFactory factory)
    {
        await factory.CreateConfirmedUserAsync("op@zwarden.test", StrongPassword);
        await AuthorizationBootstrapper.EnsureSeededAsync(factory.Services, "op@zwarden.test");
        HttpClient client = factory.CreateWebClient();
        await LoginAsync(client, "op@zwarden.test", StrongPassword);
        return client;
    }

    private static async Task<ServerId> SeedServerAsync(
        ZWardenWebAppFactory factory, string name, int? gamePort = null, int? queryPort = null)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Server server = Server.Import(AgentId.New(), ServerId.New(), name, DateTimeOffset.UtcNow);
        if (gamePort is int game && queryPort is int query)
        {
            server.RecordContainer($"pz-{name}", game, query);
        }

        db.Set<Server>().Add(server);
        await db.SaveChangesAsync();
        return server.Id;
    }

    private static async Task LoginAsync(HttpClient client, string email, string password)
    {
        HttpResponseMessage page = await client.GetAsync(new Uri("/login", UriKind.Relative));
        string html = await page.Content.ReadAsStringAsync();
        Dictionary<string, string> form = ParseHiddenInputs(html);
        form["Input.Email"] = email;
        form["Input.Password"] = password;
        await client.PostAsync(new Uri("/login", UriKind.Relative), new FormUrlEncodedContent(form));
    }

    private static Dictionary<string, string> ParseHiddenInputs(string html)
    {
        Dictionary<string, string> inputs = new(StringComparer.Ordinal);
        foreach (Match tag in Regex.Matches(html, "<input\\b[^>]*?type=\"hidden\"[^>]*?>"))
        {
            Match name = Regex.Match(tag.Value, "name=\"([^\"]+)\"");
            Match value = Regex.Match(tag.Value, "value=\"([^\"]*)\"");
            if (name.Success)
            {
                inputs[name.Groups[1].Value] = value.Success ? WebUtility.HtmlDecode(value.Groups[1].Value) : string.Empty;
            }
        }

        return inputs;
    }
}
