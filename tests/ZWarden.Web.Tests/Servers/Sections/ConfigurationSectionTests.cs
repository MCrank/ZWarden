using BlazorBlueprint.Components;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Configuration;
using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;
using ZWarden.PzConfig.Revisions;
using ZWarden.Web.Components.Pages.Servers;
using ZWarden.Web.Components.Pages.Servers.Sections;

namespace ZWarden.Web.Tests.Servers.Sections;

/// <summary>
/// The Config section as circuit state (#299; formerly static form posts plus config-editor.js, F20c/#223/#224/#226/
/// #228/#243). The live read is faked (an Agent round-trip); everything else runs through the real configuration
/// services. A drift is a second read returning a different baseline. The unsaved edits are a [PersistentState] draft
/// that survives the circuit being persisted and restored (D1).
/// </summary>
public sealed class ConfigurationSectionTests
{
    // ---- rendering -----------------------------------------------------------------------------------------------

    [Test]
    public async Task Without_a_live_read_the_offline_state_and_the_by_path_fallback_show()
    {
        // No fake reader: the real coordinator has no connected Agent, so the read is AgentOffline.
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("config-offline", hostOnline: false);
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "config");

        await Assert.That(cut.Markup).Contains("data-config-card");
        await Assert.That(cut.Find("[data-config-unavailable]").TextContent).Contains("offline");
        await Assert.That(cut.FindAll("[data-cfg-form]")).IsEmpty();
        await Assert.That(cut.Markup).Contains("data-action=\"config-apply\"");
        await Assert.That(cut.Markup).Contains("id=\"config-file\"");
        await Assert.That(cut.Markup).Contains("data-revisions-empty");
    }

    [Test]
    public async Task The_history_lists_a_recorded_revision()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("with-history");
        await SeedRevisionAsync(harness, serverId, PzConfigFile.Ini, "[[\"PublicName\",\"s:First\"]]", DateTimeOffset.UtcNow);
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "config");

        await Assert.That(cut.FindAll("[data-revision-row]")).Count().IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-revisions-empty]")).IsEmpty();
    }

    [Test]
    public async Task The_editor_renders_grouped_prefilled_controls_from_a_live_read()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Reader(SampleView(), out _));
        ServerId serverId = await harness.SeedServerAsync("live-config");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "config", "&file=SandboxVars");

        await Assert.That(cut.Markup).Contains("data-cfg-form");
        await Assert.That(cut.Markup).Contains("data-config-tabs");
        await Assert.That(cut.Markup).Contains("data-config-raw");
        await Assert.That(cut.FindAll("[data-cfg-path=PVP] input.zw-cfg-switch")).Count().IsEqualTo(1);
        await Assert.That(cut.Markup).Contains("Population");
        // A ranged-enum's option labels come from the comment (rendered as select options).
        await Assert.That(cut.Markup).Contains(">Insane<");
        // An unknown/mod key is flagged as passed through unvalidated (the "Other" section).
        await Assert.That(cut.Markup).Contains("Not in ZWarden's schema");
        await Assert.That(cut.Markup).Contains("data-config-advanced");
        // The raw editor is pre-filled with the live raw text.
        await Assert.That(cut.Markup).Contains("data-config-raweditform");
        await Assert.That(cut.Markup).Contains("VERSION = 1,");
    }

    [Test]
    public async Task A_damaged_boolean_is_a_choice_list_with_its_empty_value_and_a_healthy_one_a_switch()
    {
        // #223: a schema boolean already damaged to "" is offered as a choice with the empty value still there,
        // never an Off toggle.
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Reader(IniView(), out _));
        ServerId serverId = await harness.SeedServerAsync("ini-render");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "config", "&file=Ini");

        await Assert.That(cut.FindAll("[data-cfg-path=PVP] input.zw-cfg-switch")).Count().IsEqualTo(1);
        await Assert.That(cut.Find("[data-cfg-path=Open] select").InnerHtml).Contains(">(empty)<");
    }

    [Test]
    public async Task Managed_ports_are_read_only_and_name_the_host_port_players_use()
    {
        // #228: the INI ports are ZWarden's; the file's 16261 is the container port, not what players dial.
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
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Reader(view, out _));
        ServerId serverId = await harness.SeedServerAsync("ports", gamePort: 16265, queryPort: 16266);
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "config", "&file=Ini");

        await Assert.That(cut.Find("[data-cfg-path=DefaultPort] input").HasAttribute("readonly")).IsTrue();
        await Assert.That(cut.Find("[data-cfg-path=MaxPlayers] input").HasAttribute("readonly")).IsFalse();
        await Assert.That(cut.Find("[data-cfg-path=RCONPort] input").HasAttribute("readonly")).IsTrue();
        await Assert.That(cut.Find("[data-cfg-path=DefaultPort] [data-cfg-managed]").TextContent)
            .IsEqualTo("Managed by ZWarden · players connect on host port 16265");
        await Assert.That(cut.Find("[data-cfg-path=RCONPort] [data-cfg-managed]").TextContent)
            .IsEqualTo("Managed by ZWarden · used by ZWarden's RCON connection, not published on the host");
    }

    [Test]
    public async Task Collapse_and_expand_all_close_and_open_every_section_and_remember_the_choice()
    {
        // #243: one choice for every editor, remembered in this browser.
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Reader(SampleView(), out _));
        ServerId serverId = await harness.SeedServerAsync("cfg-collapse");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "config", "&file=SandboxVars");
        await Assert.That(cut.FindAll("details[data-cfg-sec]").All(d => d.HasAttribute("open"))).IsTrue();

        await cut.Find("[data-cfg-sections=collapse]").ClickAsync(new());

        await Assert.That(cut.FindAll("details[data-cfg-sec]").Any(d => d.HasAttribute("open"))).IsFalse();
        await Assert.That(harness.Context.JSInterop.Invocations.Any(i =>
            i.Identifier == "set" && i.Arguments.Contains("zw-cfg-sections") && i.Arguments.Contains("collapsed"))).IsTrue();

        await cut.Find("[data-cfg-sections=expand]").ClickAsync(new());

        await Assert.That(cut.FindAll("details[data-cfg-sec]").All(d => d.HasAttribute("open"))).IsTrue();
    }

    [Test]
    public async Task Search_shows_only_matching_rows_and_opens_their_sections()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Reader(SampleView(), out _));
        ServerId serverId = await harness.SeedServerAsync("cfg-search");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "config", "&file=SandboxVars");
        await cut.Find("[data-cfg-sections=collapse]").ClickAsync(new());

        await cut.Find("[data-cfg-search]").InputAsync(new() { Value = "popul" });

        await Assert.That(cut.FindAll("[data-cfg-row]").Select(r => r.GetAttribute("data-cfg-path") ?? string.Empty)).IsEquivalentTo(["Zombies"]);
        await Assert.That(cut.Find("details[data-cfg-sec]").HasAttribute("open")).IsTrue();
    }

    // ---- the structured editor -----------------------------------------------------------------------------------

    [Test]
    public async Task Apply_sends_only_the_changed_settings_with_the_baseline_the_operator_saw()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Reader(SampleView(), out _));
        ServerId serverId = await harness.SeedServerAsync("live-config-apply");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "config", "&file=SandboxVars");

        await cut.Find("[data-cfg-path=PVP] input[type=checkbox]").ChangeAsync(new() { Value = false });
        await cut.Find("[data-cfg-path=Zombies] select").ChangeAsync(new() { Value = "1" });
        await Assert.That(cut.Find("[data-cfg-path=Zombies]").ClassList).Contains("zw-cfg-changed");
        await cut.Find("[data-action=config-apply-batch]").ClickAsync(new());

        cut.WaitForState(() => harness.Payload(serverId, OperationKind.ConfigApply) is not null);
        string payload = harness.Payload(serverId, OperationKind.ConfigApply)!;
        await Assert.That(payload).Contains("PVP");
        await Assert.That(payload).Contains("false");
        await Assert.That(payload).Contains("Zombies");
        await Assert.That(payload).DoesNotContain("PublicName");
        await Assert.That(payload).DoesNotContain("XpMultiplierGlobal");
        // The live-read baseline the operator saw (ADR 0042), not the last recorded revision.
        await Assert.That(payload).Contains("hash-abc");
    }

    [Test]
    public async Task A_full_size_file_applies_just_the_one_changed_row()
    {
        // #224: a real B42 SandboxVars has ~280 settings; only the changed one is applied.
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Reader(LargeSandboxView(300), out _));
        ServerId serverId = await harness.SeedServerAsync("large-sandbox");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "config", "&file=SandboxVars");

        await cut.Find("[data-cfg-path='Custom.Key150'] input").InputAsync(new() { Value = "2" });
        await cut.Find("[data-action=config-apply-batch]").ClickAsync(new());

        cut.WaitForState(() => harness.Payload(serverId, OperationKind.ConfigApply) is not null);
        string payload = harness.Payload(serverId, OperationKind.ConfigApply)!;
        await Assert.That(payload).Contains("Custom.Key150");
        await Assert.That(payload).DoesNotContain("Custom.Key149");
    }

    [Test]
    public async Task An_unrelated_apply_leaves_an_untouched_damaged_boolean_alone()
    {
        // #223 recovery: applying another change must not write the damaged boolean.
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Reader(IniView(), out _));
        ServerId serverId = await harness.SeedServerAsync("ini-unrelated");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "config", "&file=Ini");

        await cut.Find("[data-cfg-path=MaxPlayers] input").InputAsync(new() { Value = "20" });
        await cut.Find("[data-action=config-apply-batch]").ClickAsync(new());

        cut.WaitForState(() => harness.Payload(serverId, OperationKind.ConfigApply) is not null);
        string payload = harness.Payload(serverId, OperationKind.ConfigApply)!;
        await Assert.That(payload).Contains("MaxPlayers");
        await Assert.That(payload).DoesNotContain("PVP");
        await Assert.That(payload).DoesNotContain("Open");
    }

    [Test]
    public async Task A_schema_rejection_is_shown_and_nothing_is_applied()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Reader(IniView(), out _));
        ServerId serverId = await harness.SeedServerAsync("ini-reject");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "config", "&file=Ini");

        await cut.Find("[data-cfg-path=MaxPlayers] input").InputAsync(new() { Value = "5000" }); // MaxPlayers is 1..100.
        await cut.Find("[data-action=config-apply-batch]").ClickAsync(new());

        cut.WaitForState(() => cut.FindAll("[data-config-message]").Count == 1);
        string message = cut.Find("[data-config-message]").TextContent;
        await Assert.That(message).Contains("MaxPlayers");
        await Assert.That(message).Contains("outside the allowed range");
        await Assert.That(harness.FirstOperation(serverId, OperationKind.ConfigApply)).IsNull();
    }

    [Test]
    public async Task Apply_with_no_changes_says_so_and_enqueues_nothing()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Reader(SampleView(), out _));
        ServerId serverId = await harness.SeedServerAsync("no-change");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "config", "&file=SandboxVars");

        await cut.Find("[data-action=config-apply-batch]").ClickAsync(new());

        cut.WaitForState(() => cut.FindAll("[data-config-message]").Count == 1);
        await Assert.That(cut.Find("[data-config-message]").TextContent).Contains("No changes to apply");
        await Assert.That(harness.FirstOperation(serverId, OperationKind.ConfigApply)).IsNull();
    }

    [Test]
    public async Task A_drift_refuses_keeps_the_edits_on_the_current_values_and_a_second_apply_writes_them()
    {
        // #226: the file changed on the host since it was loaded; the refused edit (Zombies 4 → 1) is carried onto
        // the fresh row, whose Original is the current host value, so it stays highlighted and Apply again writes it.
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Reader(SampleView(), out SwitchableReader reader));
        ServerId serverId = await harness.SeedServerAsync("drift-keep");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "config", "&file=SandboxVars");
        await cut.Find("[data-cfg-path=Zombies] select").ChangeAsync(new() { Value = "1" });
        reader.View = SampleView(baseline: "hash-moved");

        await cut.Find("[data-action=config-apply-batch]").ClickAsync(new());

        cut.WaitForState(() => cut.FindAll("[data-config-drift]").Count == 1);
        await Assert.That(harness.FirstOperation(serverId, OperationKind.ConfigApply)).IsNull();
        await Assert.That(cut.Find("[data-cfg-path=Zombies]").ClassList).Contains("zw-cfg-changed");

        await cut.Find("[data-action=config-apply-batch]").ClickAsync(new());

        cut.WaitForState(() => harness.Payload(serverId, OperationKind.ConfigApply) is not null);
        await Assert.That(harness.Payload(serverId, OperationKind.ConfigApply)!).Contains("hash-moved");
    }

    // ---- tracking a write (#226) ---------------------------------------------------------------------------------

    [Test]
    public async Task An_applied_change_withholds_the_stale_editor_until_the_write_finishes()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Reader(SampleView(), out _));
        ServerId serverId = await harness.SeedServerAsync("prg-done");
        IRenderedComponent<ServerDetail> cut = await ApplyZombiesChangeAsync(harness, serverId);

        cut.WaitForState(() => cut.FindAll("[data-config-applying]").Count == 1);
        await Assert.That(cut.FindAll("[data-cfg-form]")).IsEmpty();

        await FinishWriteAsync(harness, serverId, succeeded: true, "Applied and reloaded live on the running server.");

        cut.WaitForState(() => cut.FindAll("[data-config-applied]").Count == 1, TimeSpan.FromSeconds(10));
        await Assert.That(cut.Find("[data-config-applied]").TextContent).Contains("Applied and reloaded live on the running server.");
        await Assert.That(cut.FindAll("[data-cfg-form]")).Count().IsEqualTo(1);
    }

    [Test]
    public async Task A_failed_write_shows_its_reason()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Reader(SampleView(), out _));
        ServerId serverId = await harness.SeedServerAsync("prg-failed");
        IRenderedComponent<ServerDetail> cut = await ApplyZombiesChangeAsync(harness, serverId);
        cut.WaitForState(() => cut.FindAll("[data-config-applying]").Count == 1);

        await FinishWriteAsync(harness, serverId, succeeded: false, "The configuration on disk changed outside ZWarden.");

        cut.WaitForState(() => cut.FindAll("[data-config-apply-failed]").Count == 1, TimeSpan.FromSeconds(10));
        await Assert.That(cut.Find("[data-config-apply-failed]").TextContent).Contains("The configuration on disk changed outside ZWarden.");
    }

    [Test]
    public async Task An_op_link_for_another_servers_write_is_ignored()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Reader(SampleView(), out _));
        ServerId first = await harness.SeedServerAsync("op-owner");
        ServerId second = await harness.SeedServerAsync("op-other");
        await ApplyZombiesChangeAsync(harness, first);
        Operation op = harness.FirstOperation(first, OperationKind.ConfigApply)!;

        IRenderedComponent<ServerDetail> cut = harness.Render(second, "config", $"&file=SandboxVars&op={op.Id}");

        await Assert.That(cut.FindAll("[data-config-applying]")).IsEmpty();
        await Assert.That(cut.FindAll("[data-cfg-form]")).Count().IsEqualTo(1);
    }

    // ---- the by-path and raw edits, restore ----------------------------------------------------------------------

    [Test]
    public async Task The_by_path_edit_enqueues_against_the_live_reads_baseline()
    {
        // #226: the by-path form drift-checks against the file as it is now, not the last recorded revision.
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Reader(SampleView(), out _));
        ServerId serverId = await harness.SeedServerAsync("advanced-live");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "config", "&file=SandboxVars");

        await cut.Find("#config-file").ChangeAsync(new() { Value = "SandboxVars" });
        await InteractivePageHarness.TypeAsync(cut, "config-path", "Zombies");
        await cut.Find("#config-kind").ChangeAsync(new() { Value = "Number" });
        await InteractivePageHarness.TypeAsync(cut, "config-value", "3");
        await cut.Find("[data-config-advanced] form").SubmitAsync();

        cut.WaitForState(() => harness.Payload(serverId, OperationKind.ConfigApply) is not null);
        Operation op = harness.FirstOperation(serverId, OperationKind.ConfigApply)!;
        await Assert.That(op.IsMutating).IsTrue();
        await Assert.That(op.CommandPayload).Contains("Zombies");
        await Assert.That(op.CommandPayload).Contains("hash-abc");
    }

    [Test]
    public async Task Restore_re_applies_a_prior_revision()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("restorable");
        DateTimeOffset t0 = DateTimeOffset.UtcNow;
        await SeedRevisionAsync(harness, serverId, PzConfigFile.Ini, "[[\"PublicName\",\"s:First\"]]", t0);
        await SeedRevisionAsync(harness, serverId, PzConfigFile.Ini, "[[\"PublicName\",\"s:Second\"]]", t0.AddMinutes(5));
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "config");

        await cut.Find("[data-action=config-restore]").ClickAsync(new());

        cut.WaitForState(() => harness.Payload(serverId, OperationKind.ConfigApply) is not null);
        await Assert.That(harness.Payload(serverId, OperationKind.ConfigApply)!).Contains("PublicName");
    }

    [Test]
    public async Task A_raw_edit_without_the_acknowledgement_is_refused()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Reader(SampleView(), out _, raw: true));
        ServerId serverId = await harness.SeedServerAsync("raw-unconfirmed");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "config", "&file=SandboxVars");

        await TypeRawAsync(cut, "SandboxVars = {\n    Zombies = 1,\n}\n");
        await cut.Find("[data-action=config-raw-apply]").ClickAsync(new());

        cut.WaitForState(() => cut.FindAll("[data-config-message]").Count == 1);
        await Assert.That(cut.Find("[data-config-message]").TextContent).Contains("Confirm you understand");
        await Assert.That(harness.FirstOperation(serverId, OperationKind.ConfigApplyRaw)).IsNull();
    }

    [Test]
    public async Task A_confirmed_in_sync_raw_edit_stages_and_enqueues_a_raw_apply()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Reader(SampleView(), out _, raw: true));
        ServerId serverId = await harness.SeedServerAsync("raw-apply");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "config", "&file=SandboxVars");

        await TypeRawAsync(cut, "SandboxVars = {\n    Zombies = 1,\n}\n");
        await cut.Find("#config-raw-confirm").ClickAsync(new());
        await cut.Find("[data-action=config-raw-apply]").ClickAsync(new());

        cut.WaitForState(() => harness.Payload(serverId, OperationKind.ConfigApplyRaw) is not null);
        Operation op = harness.FirstOperation(serverId, OperationKind.ConfigApplyRaw)!;
        await Assert.That(op.IsMutating).IsTrue();
        // The command carries the file, the live-read baseline and the staging correlation id.
        await Assert.That(op.CommandPayload).Contains("hash-abc");
        await Assert.That(op.CommandPayload).Contains("corr-fake");
    }

    [Test]
    public async Task A_raw_edit_against_a_moved_file_shows_the_drift_banner_and_enqueues_nothing()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Reader(SampleView(), out SwitchableReader reader, raw: true));
        ServerId serverId = await harness.SeedServerAsync("raw-drift");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "config", "&file=SandboxVars");
        reader.View = SampleView(baseline: "hash-moved");

        await TypeRawAsync(cut, "SandboxVars = {\n    Zombies = 1,\n}\n");
        await cut.Find("#config-raw-confirm").ClickAsync(new());
        await cut.Find("[data-action=config-raw-apply]").ClickAsync(new());

        cut.WaitForState(() => cut.FindAll("[data-config-drift]").Count == 1);
        await Assert.That(harness.FirstOperation(serverId, OperationKind.ConfigApplyRaw)).IsNull();
    }

    // ---- the persisted draft (D1, #322) --------------------------------------------------------------------------
    // The end-to-end pause/resume is a browser test: ServerDetailSmokeTests.Config_keeps_an_unsaved_edit_across_a_
    // circuit_pause_and_resume. These pin what is persisted, and when.

    [Test]
    public async Task The_prerender_persists_no_editor_state()
    {
        // #322: a prerender's state rides in the page and an enhanced navigation posts it to the circuit, where a
        // real-size file's editor state broke the hub's 32 KB receive limit and dropped the connection.
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(
            Reader(LargeSandboxView(300), out _), prerendering: true);
        ServerId serverId = await harness.SeedServerAsync("draft-prerender");
        harness.Render(serverId, "config", "&file=SandboxVars");

        harness.State.TriggerOnPersisting();

        await Assert.That(harness.State.TryTake(DraftStateKey, out ConfigDraftSnapshot? _)).IsFalse();
    }

    [Test]
    public async Task A_circuit_with_no_unsaved_edits_persists_nothing()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Reader(SampleView(), out _));
        ServerId serverId = await harness.SeedServerAsync("draft-clean");
        harness.Render(serverId, "config", "&file=SandboxVars");

        harness.State.TriggerOnPersisting();

        await Assert.That(harness.State.TryTake(DraftStateKey, out ConfigDraftSnapshot? _)).IsFalse();
    }

    [Test]
    public async Task A_circuit_persists_only_the_unsaved_edits_with_the_baseline_the_operator_saw()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Reader(LargeSandboxView(300), out _));
        ServerId serverId = await harness.SeedServerAsync("draft-edits");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "config", "&file=SandboxVars");
        await cut.Find("[data-cfg-path='Custom.Key150'] input").InputAsync(new() { Value = "2" });

        harness.State.TriggerOnPersisting();

        await Assert.That(harness.State.TryTake(DraftStateKey, out ConfigDraftSnapshot? snapshot)).IsTrue();
        await Assert.That(snapshot!.File).IsEqualTo(PzConfigFile.SandboxVars);
        await Assert.That(snapshot.Edits.Select(e => (e.Path, e.Value))).IsEquivalentTo([("Custom.Key150", "2")]);
        await Assert.That(snapshot.BaselineHash).IsEqualTo(LargeSandboxView(300).BaselineHash);
    }

    [Test]
    public async Task A_restored_draft_shows_the_kept_edit_over_a_fresh_read_and_applies_it()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Reader(SampleView(), out _));
        ServerId serverId = await harness.SeedServerAsync("draft-restore");
        harness.State.Persist(DraftStateKey, new ConfigDraftSnapshot(
            PzConfigFile.SandboxVars, "hash-abc", [new ConfigApplyEdit("Zombies", ConfigEditKind.Number, "1")], null, false));

        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "config", "&file=SandboxVars");

        await Assert.That(cut.Find("[data-cfg-path=Zombies]").ClassList).Contains("zw-cfg-changed");
        await cut.Find("[data-action=config-apply-batch]").ClickAsync(new());
        cut.WaitForState(() => harness.Payload(serverId, OperationKind.ConfigApply) is not null);
        await Assert.That(harness.Payload(serverId, OperationKind.ConfigApply)!).Contains("Zombies");
    }

    [Test]
    public async Task A_restored_draft_whose_file_changed_on_the_host_refuses_the_apply_as_drift()
    {
        // The snapshot keeps the baseline the operator was shown, so a write made while the circuit was gone is caught.
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(
            Reader(SampleView(baseline: "hash-moved"), out _));
        ServerId serverId = await harness.SeedServerAsync("draft-restore-drift");
        harness.State.Persist(DraftStateKey, new ConfigDraftSnapshot(
            PzConfigFile.SandboxVars, "hash-abc", [new ConfigApplyEdit("Zombies", ConfigEditKind.Number, "1")], null, false));

        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "config", "&file=SandboxVars");
        await cut.Find("[data-action=config-apply-batch]").ClickAsync(new());

        cut.WaitForState(() => cut.FindAll("[data-config-drift]").Count == 1);
        await Assert.That(harness.FirstOperation(serverId, OperationKind.ConfigApply)).IsNull();
        await Assert.That(cut.Find("[data-cfg-path=Zombies]").ClassList).Contains("zw-cfg-changed");
    }

    [Test]
    public async Task A_kept_draft_for_another_file_is_not_carried_onto_this_one()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Reader(SampleView(), out _));
        ServerId serverId = await harness.SeedServerAsync("draft-other-file");
        harness.State.Persist(DraftStateKey, new ConfigDraftSnapshot(
            PzConfigFile.Ini, "hash-abc", [new ConfigApplyEdit("Zombies", ConfigEditKind.Number, "1")], null, false));

        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "config", "&file=SandboxVars");

        await Assert.That(cut.Find("[data-cfg-path=Zombies]").ClassList).DoesNotContain("zw-cfg-changed");
    }

    // ---- unsaved edits across sections and files (#322 live pass) -------------------------------------------------

    [Test]
    public async Task An_unsaved_edit_is_still_there_after_opening_another_section_and_coming_back()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Reader(IniView(), out SwitchableReader reader));
        ServerId serverId = await harness.SeedServerAsync("draft-keep-section");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "config");
        await cut.Find("[data-cfg-path=PVP] input[type=checkbox]").ChangeAsync(new() { Value = false });

        await NavigateAsync(harness, cut, $"/servers/{serverId}?section=logs");
        cut.WaitForState(() => cut.FindAll("[data-cfg-form]").Count == 0);
        await NavigateAsync(harness, cut, $"/servers/{serverId}?section=config");
        cut.WaitForState(() => cut.FindAll("[data-cfg-path=PVP]").Count == 1);

        await Assert.That(cut.Find("[data-cfg-path=PVP]").ClassList).Contains("zw-cfg-changed");
        await Assert.That(cut.Find("[data-cfg-path=PVP] input[type=checkbox]").HasAttribute("checked")).IsFalse();
        await cut.Find("[data-action=config-apply-batch]").ClickAsync(new());
        cut.WaitForState(() => harness.Payload(serverId, OperationKind.ConfigApply) is not null);
        await Assert.That(harness.Payload(serverId, OperationKind.ConfigApply)!).Contains("PVP");
    }

    [Test]
    public async Task An_unsaved_edit_is_still_there_after_switching_file_tabs_and_back()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Reader(SampleView(), out _));
        ServerId serverId = await harness.SeedServerAsync("draft-keep-tab");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "config", "&file=SandboxVars");
        await cut.Find("[data-cfg-path=Zombies] select").ChangeAsync(new() { Value = "1" });

        await NavigateAsync(harness, cut, $"/servers/{serverId}?section=config&file=Ini");
        cut.WaitForState(() => cut.Find("[data-config-tab=Ini]").ClassList.Contains("active"));
        await Assert.That(cut.Find("[data-cfg-path=Zombies]").ClassList).DoesNotContain("zw-cfg-changed");
        await NavigateAsync(harness, cut, $"/servers/{serverId}?section=config&file=SandboxVars");
        cut.WaitForState(() => cut.Find("[data-config-tab=SandboxVars]").ClassList.Contains("active"));

        await Assert.That(cut.Find("[data-cfg-path=Zombies]").ClassList).Contains("zw-cfg-changed");
    }

    [Test]
    public async Task An_applied_edit_is_not_shown_as_unsaved_on_coming_back()
    {
        // Once applied, the edit is on its way to the host: coming back reads the file fresh, not the old draft.
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Reader(SampleView(), out _));
        ServerId serverId = await harness.SeedServerAsync("draft-applied");
        IRenderedComponent<ServerDetail> cut = await ApplyZombiesChangeAsync(harness, serverId);

        await NavigateAsync(harness, cut, $"/servers/{serverId}?section=logs");
        cut.WaitForState(() => cut.FindAll("[data-cfg-form]").Count == 0);
        await NavigateAsync(harness, cut, $"/servers/{serverId}?section=config&file=SandboxVars");
        cut.WaitForState(() => cut.FindAll("[data-cfg-path=Zombies]").Count == 1);

        await Assert.That(cut.Find("[data-cfg-path=Zombies]").ClassList).DoesNotContain("zw-cfg-changed");
    }

    // ---- warning before unsaved edits are lost (#331) ------------------------------------------------------------
    // The prompts themselves are the browser's (unsaved-guard.js): ServerDetailSmokeTests.Unsaved_config_edits_warn_
    // before_a_reload_or_leaving_the_page. These pin when the page turns the warning on and off.

    [Test]
    public async Task The_warning_is_on_only_while_there_are_unsaved_edits_on_any_section()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Reader(SampleView(), out _));
        BunitJSModuleInterop guard = harness.Context.JSInterop.SetupModule(UnsavedConfigGuard.ModulePath);
        ServerId serverId = await harness.SeedServerAsync("guard-on");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "config", "&file=SandboxVars");
        cut.WaitForAssertion(() => guard.VerifyInvoke("set"));
        await Assert.That(GuardPath(guard)).IsNull();

        await cut.Find("[data-cfg-path=Zombies] select").ChangeAsync(new() { Value = "1" });
        cut.WaitForAssertion(() => _ = GuardPath(guard) ?? throw new InvalidOperationException("The warning is off."));
        await Assert.That(GuardPath(guard)).IsEqualTo($"/servers/{serverId}");

        // Kept while another section is open, so a reload there would lose them too.
        await NavigateAsync(harness, cut, $"/servers/{serverId}?section=logs");
        cut.WaitForState(() => cut.FindAll("[data-cfg-form]").Count == 0);
        cut.WaitForAssertion(() => _ = GuardPath(guard) ?? throw new InvalidOperationException("The warning is off."));
    }

    [Test]
    public async Task Once_applied_the_edits_no_longer_warn()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Reader(SampleView(), out _));
        BunitJSModuleInterop guard = harness.Context.JSInterop.SetupModule(UnsavedConfigGuard.ModulePath);
        ServerId serverId = await harness.SeedServerAsync("guard-applied");
        IRenderedComponent<ServerDetail> cut = await ApplyZombiesChangeAsync(harness, serverId);

        cut.WaitForAssertion(() =>
        {
            if (GuardPath(guard) is not null)
            {
                throw new InvalidOperationException("Still warning about an applied edit.");
            }
        });
    }

    [Test]
    public async Task A_page_with_nothing_unsaved_never_turns_the_warning_on()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Reader(SampleView(), out _));
        BunitJSModuleInterop guard = harness.Context.JSInterop.SetupModule(UnsavedConfigGuard.ModulePath);
        ServerId serverId = await harness.SeedServerAsync("guard-clean");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "config", "&file=SandboxVars");

        await NavigateAsync(harness, cut, $"/servers/{serverId}?section=logs");
        cut.WaitForState(() => cut.FindAll("[data-cfg-form]").Count == 0);

        await Assert.That(guard.Invocations["set"].All(i => i.Arguments[0] is null)).IsTrue();
    }

    [Test]
    public async Task A_held_link_asks_in_a_dialog_and_stay_keeps_the_page_and_the_edits()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Reader(SampleView(), out _));
        harness.Context.JSInterop.SetupModule(UnsavedConfigGuard.ModulePath);
        ServerId serverId = await harness.SeedServerAsync("guard-stay");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "config", "&file=SandboxVars");
        await cut.Find("[data-cfg-path=Zombies] select").ChangeAsync(new() { Value = "1" });
        string here = harness.Context.Services.GetRequiredService<NavigationManager>().Uri;

        await HoldLinkAsync(cut, "http://localhost/servers");
        cut.WaitForState(() => cut.FindAll("[data-unsaved-dialog]").Count == 1);
        await cut.Find("[data-action=unsaved-stay]").ClickAsync(new());

        cut.WaitForState(() => cut.FindAll("[data-unsaved-dialog]").Count == 0);
        await Assert.That(harness.Context.Services.GetRequiredService<NavigationManager>().Uri).IsEqualTo(here);
        await Assert.That(cut.Find("[data-cfg-path=Zombies]").ClassList).Contains("zw-cfg-changed");
    }

    [Test]
    public async Task Discard_and_leave_turns_the_warning_off_and_follows_the_link()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Reader(SampleView(), out _));
        BunitJSModuleInterop guard = harness.Context.JSInterop.SetupModule(UnsavedConfigGuard.ModulePath);
        ServerId serverId = await harness.SeedServerAsync("guard-leave");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "config", "&file=SandboxVars");
        await cut.Find("[data-cfg-path=Zombies] select").ChangeAsync(new() { Value = "1" });

        await HoldLinkAsync(cut, "http://localhost/servers");
        cut.WaitForState(() => cut.FindAll("[data-unsaved-dialog]").Count == 1);
        await cut.Find("[data-action=unsaved-leave]").ClickAsync(new());

        await Assert.That(harness.Context.Services.GetRequiredService<NavigationManager>().Uri).IsEqualTo("http://localhost/servers");
        await Assert.That(GuardPath(guard)).IsNull();
    }

    // What unsaved-guard.js does with a link to another page: hands it to the page's guard.
    private static Task HoldLinkAsync(IRenderedComponent<ServerDetail> cut, string href) =>
        ((IRenderedComponent<IComponent>)cut).FindComponents<UnsavedConfigGuard>().Single().Instance.AskToLeave(href);

    // The Server page the browser-side warning was last turned on for, or null when it is off.
    private static string? GuardPath(BunitJSModuleInterop guard) =>
        guard.Invocations["set"] is { Count: > 0 } calls ? calls[^1].Arguments[0] as string : null;

    // A rail or tab click: an enhanced navigation the page sees as a query change.
    private static Task NavigateAsync(InteractivePageHarness harness, IRenderedComponent<ServerDetail> cut, string url) =>
        cut.InvokeAsync(() => harness.Context.Services.GetRequiredService<NavigationManager>().NavigateTo(url));

    // The persisted-state key the Config section keeps its unsaved edits under.
    private const string DraftStateKey = "zw-config-draft";

    // ---- helpers -------------------------------------------------------------------------------------------------

    // Applies Zombies 4 → 1 through the editor, in sync with SampleView's baseline.
    private static async Task<IRenderedComponent<ServerDetail>> ApplyZombiesChangeAsync(InteractivePageHarness harness, ServerId serverId)
    {
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "config", "&file=SandboxVars");
        await cut.Find("[data-cfg-path=Zombies] select").ChangeAsync(new() { Value = "1" });
        await cut.Find("[data-action=config-apply-batch]").ClickAsync(new());
        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.ConfigApply) is not null);
        return cut;
    }

    // Drives the Server's pending config write to a terminal state, as the Agent's completion would (#226).
    private static async Task FinishWriteAsync(InteractivePageHarness harness, ServerId serverId, bool succeeded, string text)
    {
        await using AsyncServiceScope scope = harness.Factory.Services.CreateSystemScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Operation op = db.Set<Operation>().First(o => o.ServerId == serverId && o.Kind == OperationKind.ConfigApply);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (op.State == OperationState.Pending)
        {
            op.MarkDispatched(now.AddMinutes(5), now); // the dispatcher already did, when the host is connected
        }

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

    private static async Task TypeRawAsync(IRenderedComponent<ServerDetail> cut, string text)
    {
        IRenderedComponent<BbTextarea> raw = ((IRenderedComponent<IComponent>)cut).FindComponents<BbTextarea>().Single();
        await cut.InvokeAsync(() => raw.Instance.ValueChanged.InvokeAsync(text));
    }

    private static async Task SeedRevisionAsync(
        InteractivePageHarness harness, ServerId server, PzConfigFile file, string canonicalText, DateTimeOffset at)
    {
        await using AsyncServiceScope scope = harness.Factory.Services.CreateSystemScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        PzValueSnapshot snapshot = PzValueSnapshot.Parse(canonicalText);
        db.Set<ConfigurationRevision>().Add(ConfigurationRevision.Record(server, file, snapshot.CanonicalText, snapshot.Hash, at));
        await db.SaveChangesAsync();
    }

    private static Action<IServiceCollection> Reader(ConfigDocumentView view, out SwitchableReader reader, bool raw = false)
    {
        var fake = new SwitchableReader(view);
        reader = fake;
        return s =>
        {
            s.AddSingleton<IServerConfigurationReader>(fake);
            if (raw)
            {
                // A staging channel that reports the text reached the (fake) host, so the editor enqueues the apply.
                s.AddSingleton<IServerConfigRawEditChannel>(new FakeRawEditChannel());
            }
        };
    }

    private static ConfigDocumentView SampleView(string baseline = "hash-abc") => new(
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
        baseline,
        [],
        null);

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

    /// <summary>A live read whose result a test can change (a second read after the file moved on the host).</summary>
    internal sealed class SwitchableReader(ConfigDocumentView view) : IServerConfigurationReader
    {
        private int _reads;

        public ConfigDocumentView View { get; set; } = view;

        public int Reads => _reads;

        public Task<ConfigDocumentView> ReadAsync(
            UserId user, ServerId server, PzConfigFile file, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _reads);
            return Task.FromResult(View);
        }
    }

    private sealed class FakeRawEditChannel : IServerConfigRawEditChannel
    {
        public Task<ConfigRawEditStage> StageAsync(
            ServerId server, AgentId owningAgent, string rawText, CancellationToken cancellationToken = default) =>
            Task.FromResult(ConfigRawEditStage.Ok("corr-fake"));
    }
}
