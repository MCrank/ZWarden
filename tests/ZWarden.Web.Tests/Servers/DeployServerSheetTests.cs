using Bunit;
using Bunit.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Agents;
using ZWarden.Application.Servers;
using ZWarden.Domain.Agents;
using ZWarden.Domain.Enrollments;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;
using ZWarden.Web.Components.Servers;

namespace ZWarden.Web.Tests.Servers;

/// <summary>
/// #338: the Fleet board's "Deploy server" sheet, which replaces the #230 inline Register form. Basics → Game version →
/// Memory with Back / Next / Deploy; Next refuses an invalid step; Deploy registers through the real Application service
/// (the #230/#258 wizard tests, ported from static form POSTs) and the island lists the tenant's Hosts by name.
/// </summary>
public sealed class DeployServerSheetTests
{
    private const string AgentHash = "agenthashaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const long GiB = 1024L * 1024 * 1024;

    [Test]
    public async Task The_button_opens_the_sheet_on_basics_and_closing_it_hides_it()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        IRenderedComponent<DeployServerSheet> cut = harness.Context.Render<DeployServerSheet>();
        await Assert.That(cut.FindAll("[data-deploy-server-sheet]")).IsEmpty();

        await cut.Find("[data-action=deploy-server-open]").ClickAsync(new());

        cut.WaitForState(() => cut.FindAll("[data-deploy-step=basics]").Count == 1);
        await Assert.That(cut.Find("[data-step='0']").GetAttribute("aria-current")).IsEqualTo("step");
        await Assert.That(cut.FindAll("[data-action=step-back]")).IsEmpty();
    }

    [Test]
    public async Task Open_on_load_opens_the_sheet_for_the_deploy_deep_link()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();

        IRenderedComponent<DeployServerSheet> cut = harness.Context.Render<DeployServerSheet>(p => p.Add(c => c.OpenOnLoad, true));

        cut.WaitForState(() => cut.FindAll("[data-deploy-server-sheet]").Count == 1);
    }

    [Test]
    public async Task The_host_picker_names_each_host_with_its_free_memory_and_disables_an_offline_one()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        AgentId online = await SeedHostAsync(harness, "host-alpha", connected: true);
        RecordCapacity(harness, online, new HostCapacity(online, 32 * GiB, 10 * GiB, 6 * GiB, 4 * GiB, 2 * GiB, DateTimeOffset.UtcNow));
        AgentId offline = await SeedHostAsync(harness, "host-zulu", connected: false);

        IRenderedComponent<DeployServerSheet> cut = await OpenAsync(harness);

        AngleSharp.Dom.IElement on = cut.Find($"#deploy-host option[value='{online}']");
        AngleSharp.Dom.IElement off = cut.Find($"#deploy-host option[value='{offline}']");
        await Assert.That(on.TextContent).IsEqualTo("host-alpha — 20 GiB free");
        await Assert.That(on.HasAttribute("disabled")).IsFalse();
        await Assert.That(off.TextContent).IsEqualTo("host-zulu — offline");
        await Assert.That(off.HasAttribute("disabled")).IsTrue();
        // The one connected host is preselected: there's nothing to choose.
        await Assert.That(cut.FindComponents<BlazorBlueprint.Components.BbNativeSelect<string>>().Single().Instance.Value)
            .IsEqualTo(online.ToString());
    }

    [Test]
    public async Task Next_refuses_basics_without_a_name_and_says_why()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        await SeedHostAsync(harness, "host-alpha", connected: true);
        IRenderedComponent<DeployServerSheet> cut = await OpenAsync(harness);

        await cut.Find("[data-action=step-next]").ClickAsync(new());

        cut.WaitForState(() => cut.FindAll("[data-deploy-message]").Count == 1);
        await Assert.That(cut.Find("[data-deploy-message]").TextContent).Contains("Choose a host and a name.");
        await Assert.That(cut.FindAll("[data-deploy-step=basics]").Count).IsEqualTo(1);
    }

    [Test]
    public async Task Next_refuses_an_invalid_game_port()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        await SeedHostAsync(harness, "host-alpha", connected: true);
        IRenderedComponent<DeployServerSheet> cut = await OpenAsync(harness);
        await InteractivePageHarness.TypeAsync(cut, "deploy-name", "bad-port");
        await InteractivePageHarness.TypeAsync(cut, "deploy-port", "80");

        await cut.Find("[data-action=step-next]").ClickAsync(new());

        cut.WaitForState(() => cut.FindAll("[data-deploy-message]").Count == 1);
        await Assert.That(cut.Find("[data-deploy-message]").TextContent).Contains("between 1024 and 65534");
    }

    [Test]
    public async Task Next_refuses_a_setting_that_could_break_the_config_line()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        await SeedHostAsync(harness, "host-alpha", connected: true);
        IRenderedComponent<DeployServerSheet> cut = await OpenAsync(harness);
        await InteractivePageHarness.TypeAsync(cut, "deploy-name", "sneaky");
        await InteractivePageHarness.TypeAsync(cut, "deploy-welcome", "hi\nRCONPassword=x");

        await cut.Find("[data-action=step-next]").ClickAsync(new());

        cut.WaitForState(() => cut.FindAll("[data-deploy-message]").Count == 1);
        await Assert.That(cut.Find("[data-deploy-message]").TextContent).Contains("printable");
    }

    [Test]
    public async Task Back_returns_to_basics_with_the_typed_values_kept()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        await SeedHostAsync(harness, "host-alpha", connected: true);
        IRenderedComponent<DeployServerSheet> cut = await OpenAsync(harness);
        await InteractivePageHarness.TypeAsync(cut, "deploy-name", "kept");
        await NextAsync(cut, "game-version");

        await cut.Find("[data-action=step-back]").ClickAsync(new());

        cut.WaitForState(() => cut.FindAll("[data-deploy-step=basics]").Count == 1);
        await Assert.That(cut.FindComponents<BlazorBlueprint.Components.BbInput>().Single(i => i.Instance.Id == "deploy-name").Instance.Value)
            .IsEqualTo("kept");
    }

    [Test]
    public async Task The_game_version_step_offers_the_curated_branches_with_public_first_and_custom_last()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        IRenderedComponent<DeployServerSheet> cut = await ToGameVersionAsync(harness, "branches");

        string[] values = [.. cut.FindAll("#deploy-branch option").Select(o => o.GetAttribute("value") ?? string.Empty)];
        await Assert.That(values[0]).IsEqualTo(string.Empty);
        await Assert.That(values).Contains("unstable");
        await Assert.That(values).Contains("42.19");
        await Assert.That(values[^1]).IsEqualTo(ServerBranchView.CustomChoice);
        await Assert.That(cut.FindAll("[data-branch-help]").Count).IsEqualTo(1);
        // The custom field shows only with Custom… picked.
        await Assert.That(cut.FindAll("#deploy-custom-branch")).IsEmpty();
        await cut.Find("#deploy-branch").ChangeAsync(new() { Value = ServerBranchView.CustomChoice });
        cut.WaitForState(() => cut.FindAll("#deploy-custom-branch").Count == 1);
    }

    [Test]
    public async Task Next_refuses_build_41_as_a_custom_branch()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        IRenderedComponent<DeployServerSheet> cut = await ToGameVersionAsync(harness, "old");
        await cut.Find("#deploy-branch").ChangeAsync(new() { Value = ServerBranchView.CustomChoice });
        cut.WaitForState(() => cut.FindAll("#deploy-custom-branch").Count == 1);
        await InteractivePageHarness.TypeAsync(cut, "deploy-custom-branch", "legacy41");

        await cut.Find("[data-action=step-next]").ClickAsync(new());

        cut.WaitForState(() => cut.FindAll("[data-deploy-message]").Count == 1);
        await Assert.That(cut.Find("[data-deploy-message]").TextContent).Contains("Build 42 only");
    }

    [Test]
    public async Task The_memory_step_shows_each_connected_hosts_free_memory()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        AgentId agent = await SeedHostAsync(harness, "host-alpha", connected: true);
        RecordCapacity(harness, agent, new HostCapacity(agent, 32 * GiB, 10 * GiB, 6 * GiB, 4 * GiB, 2 * GiB, DateTimeOffset.UtcNow));
        IRenderedComponent<DeployServerSheet> cut = await ToGameVersionAsync(harness, "memory");

        await NextAsync(cut, "memory");

        await Assert.That(cut.Find($"[data-host-capacity-line='{agent}']").TextContent).Contains("20 GiB free for new servers of 32 GiB");
        // Both fields start blank (the suggestion / the host default), bound to the draft.
        IEnumerable<BlazorBlueprint.Components.BbInput> inputs = cut.FindComponents<BlazorBlueprint.Components.BbInput>().Select(c => c.Instance);
        await Assert.That(inputs.Single(i => i.Id == "deploy-players").Value).IsNull();
        await Assert.That(inputs.Single(i => i.Id == "deploy-heap").Value).IsNull();
    }

    [Test]
    public async Task Deploy_registers_the_suggested_heap_and_the_settings_with_the_password_encrypted()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        await SeedHostAsync(harness, "host-alpha", connected: true);
        IRenderedComponent<DeployServerSheet> cut = await OpenAsync(harness);
        await InteractivePageHarness.TypeAsync(cut, "deploy-name", "friends");
        await InteractivePageHarness.TypeAsync(cut, "deploy-public-name", "Friends of Knox");
        await InteractivePageHarness.TypeAsync(cut, "deploy-max-players", "12");
        await InteractivePageHarness.TypeAsync(cut, "deploy-password", "hunter2");
        await InteractivePageHarness.TypeAsync(cut, "deploy-welcome", "Be nice");
        await cut.Find("#deploy-public").ClickAsync(new());
        await NextAsync(cut, "game-version");
        await NextAsync(cut, "memory");
        await InteractivePageHarness.TypeAsync(cut, "deploy-players", "8");

        await cut.Find("[data-action=step-finish]").ClickAsync(new());

        Operation provision = await WaitForProvisionAsync(harness, cut);
        await Assert.That(provision.CommandPayload!).DoesNotContain("hunter2");
        ServerContainerPayload payload = ServerContainerPayload.FromJson(provision.CommandPayload!);
        await Assert.That(payload.HeapSizeBytes).IsEqualTo(6 * GiB);
        await Assert.That(payload.Settings!.Public!.Value).IsTrue();
        await Assert.That(payload.Settings.PublicName).IsEqualTo("Friends of Knox");
        await Assert.That(payload.Settings.MaxPlayers).IsEqualTo(12);
        await Assert.That(payload.Settings.WelcomeMessage).IsEqualTo("Be nice");
        await Assert.That(payload.Settings.ProtectedPassword).IsNotNull();
    }

    [Test]
    public async Task A_successful_deploy_closes_the_sheet_toasts_and_refreshes_the_board()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        await SeedHostAsync(harness, "host-alpha", connected: true);
        IRenderedComponent<DeployServerSheet> cut = await ToGameVersionAsync(harness, "toasted");
        await NextAsync(cut, "memory");

        await cut.Find("[data-action=step-finish]").ClickAsync(new());

        cut.WaitForState(() => cut.FindAll("[data-deploy-server-sheet]").Count == 0);
        await Assert.That(cut.Markup).Contains("toasted is deploying on host-alpha");
        // NavigationManager.Refresh reloads the page in place (history replaced). In a circuit that is an enhanced
        // refresh, so the board gains the new row while this island and its toast stay (the browser smoke test).
        BunitNavigationManager nav = harness.Context.Services.GetRequiredService<BunitNavigationManager>();
        await Assert.That(nav.History.Count).IsEqualTo(1);
        await Assert.That(nav.History.First().Options.ReplaceHistoryEntry).IsTrue();
    }

    [Test]
    public async Task Deploy_registers_the_game_port()
    {
        // #229: the operator may pick the host pair; blank leaves it to the Agent's next free stride.
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        await SeedHostAsync(harness, "host-alpha", connected: true);
        IRenderedComponent<DeployServerSheet> cut = await OpenAsync(harness);
        await InteractivePageHarness.TypeAsync(cut, "deploy-name", "on-27015");
        await InteractivePageHarness.TypeAsync(cut, "deploy-port", "27015");
        await NextAsync(cut, "game-version");
        await NextAsync(cut, "memory");

        await cut.Find("[data-action=step-finish]").ClickAsync(new());

        Operation provision = await WaitForProvisionAsync(harness, cut);
        await Assert.That(ServerContainerPayload.FromJson(provision.CommandPayload!).GamePort).IsEqualTo(27015);
    }

    [Test]
    public async Task Deploy_registers_a_pinned_branch_and_records_it_on_the_server()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        await SeedHostAsync(harness, "host-alpha", connected: true);
        IRenderedComponent<DeployServerSheet> cut = await ToGameVersionAsync(harness, "pinned");
        await cut.Find("#deploy-branch").ChangeAsync(new() { Value = "42.19" });
        await NextAsync(cut, "memory");

        await cut.Find("[data-action=step-finish]").ClickAsync(new());

        Operation provision = await WaitForProvisionAsync(harness, cut);
        await Assert.That(ServerContainerPayload.FromJson(provision.CommandPayload!).Branch).IsEqualTo("42.19");
        await using AsyncServiceScope scope = harness.Factory.Services.CreateSystemScope();
        await Assert.That((await scope.ServiceProvider.GetRequiredService<ZWardenDbContext>().Set<Server>().SingleAsync()).Branch)
            .IsEqualTo("42.19");
    }

    [Test]
    public async Task Deploy_registers_a_custom_branch()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        await SeedHostAsync(harness, "host-alpha", connected: true);
        IRenderedComponent<DeployServerSheet> cut = await ToGameVersionAsync(harness, "custom");
        await cut.Find("#deploy-branch").ChangeAsync(new() { Value = ServerBranchView.CustomChoice });
        cut.WaitForState(() => cut.FindAll("#deploy-custom-branch").Count == 1);
        await InteractivePageHarness.TypeAsync(cut, "deploy-custom-branch", "my-test");
        await NextAsync(cut, "memory");

        await cut.Find("[data-action=step-finish]").ClickAsync(new());

        Operation provision = await WaitForProvisionAsync(harness, cut);
        await Assert.That(ServerContainerPayload.FromJson(provision.CommandPayload!).Branch).IsEqualTo("my-test");
    }

    [Test]
    public async Task Over_the_hosts_free_memory_deploy_warns_and_creates_only_once_acknowledged()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        AgentId agent = await SeedHostAsync(harness, "host-alpha", connected: true);
        RecordCapacity(harness, agent, new HostCapacity(agent, 16 * GiB, 10 * GiB, 6 * GiB, 4 * GiB, 2 * GiB, DateTimeOffset.UtcNow));
        IRenderedComponent<DeployServerSheet> cut = await ToGameVersionAsync(harness, "big");
        await NextAsync(cut, "memory");
        await InteractivePageHarness.TypeAsync(cut, "deploy-heap", "8");

        await cut.Find("[data-action=step-finish]").ClickAsync(new());

        cut.WaitForState(() => cut.FindAll("[data-overcommit-warning]").Count == 1);
        await Assert.That(await ServerCountAsync(harness)).IsEqualTo(0);

        await cut.Find("#deploy-acknowledge").ClickAsync(new());
        await cut.Find("[data-action=step-finish]").ClickAsync(new());

        await WaitForProvisionAsync(harness, cut);
    }

    [Test]
    public async Task Changing_the_heap_drops_the_overcommit_warning_and_its_acknowledgement()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        AgentId agent = await SeedHostAsync(harness, "host-alpha", connected: true);
        RecordCapacity(harness, agent, new HostCapacity(agent, 16 * GiB, 10 * GiB, 6 * GiB, 4 * GiB, 2 * GiB, DateTimeOffset.UtcNow));
        IRenderedComponent<DeployServerSheet> cut = await ToGameVersionAsync(harness, "big");
        await NextAsync(cut, "memory");
        await InteractivePageHarness.TypeAsync(cut, "deploy-heap", "8");
        await cut.Find("[data-action=step-finish]").ClickAsync(new());
        cut.WaitForState(() => cut.FindAll("[data-overcommit-warning]").Count == 1);

        await InteractivePageHarness.TypeAsync(cut, "deploy-heap", "9");

        cut.WaitForState(() => cut.FindAll("[data-overcommit-warning]").Count == 0);
    }

    [Test]
    public async Task The_overcommit_warning_keeps_the_chosen_branch_and_host()
    {
        // Live pass on #258: the static warning re-render silently fell back to the first branch and host.
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        AgentId first = await SeedHostAsync(harness, "host-alpha", connected: true);
        AgentId second = await SeedHostAsync(harness, "host-bravo", connected: true);
        RecordCapacity(harness, second, new HostCapacity(second, 16 * GiB, 10 * GiB, 6 * GiB, 4 * GiB, 2 * GiB, DateTimeOffset.UtcNow));
        IRenderedComponent<DeployServerSheet> cut = await OpenAsync(harness);
        await cut.Find("#deploy-host").ChangeAsync(new() { Value = second.ToString() });
        await InteractivePageHarness.TypeAsync(cut, "deploy-name", "pinned-big");
        await NextAsync(cut, "game-version");
        await cut.Find("#deploy-branch").ChangeAsync(new() { Value = "42.19" });
        await NextAsync(cut, "memory");
        await InteractivePageHarness.TypeAsync(cut, "deploy-heap", "8");
        await cut.Find("[data-action=step-finish]").ClickAsync(new());
        cut.WaitForState(() => cut.FindAll("[data-overcommit-warning]").Count == 1);

        await cut.Find("#deploy-acknowledge").ClickAsync(new());
        await cut.Find("[data-action=step-finish]").ClickAsync(new());

        Operation provision = await WaitForProvisionAsync(harness, cut);
        await Assert.That(provision.AgentId).IsEqualTo(second);
        await Assert.That(provision.AgentId).IsNotEqualTo(first);
        await Assert.That(ServerContainerPayload.FromJson(provision.CommandPayload!).Branch).IsEqualTo("42.19");
    }

    [Test]
    public async Task A_port_already_used_on_the_host_sends_the_operator_back_to_basics()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        AgentId agent = await SeedHostAsync(harness, "host-alpha", connected: true);
        await using (AsyncServiceScope scope = harness.Factory.Services.CreateSystemScope())
        {
            ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
            Server taken = Server.Import(agent, ServerId.New(), "taken", DateTimeOffset.UtcNow);
            taken.RecordContainer("pz-taken", 27015, 27016);
            db.Set<Server>().Add(taken);
            await db.SaveChangesAsync();
        }

        IRenderedComponent<DeployServerSheet> cut = await OpenAsync(harness);
        await InteractivePageHarness.TypeAsync(cut, "deploy-name", "clash");
        await InteractivePageHarness.TypeAsync(cut, "deploy-port", "27015");
        await NextAsync(cut, "game-version");
        await NextAsync(cut, "memory");

        await cut.Find("[data-action=step-finish]").ClickAsync(new());

        cut.WaitForState(() => cut.FindAll("[data-deploy-step=basics]").Count == 1);
        await Assert.That(cut.Find("[data-deploy-message]").TextContent).Contains("already used");
    }

    [Test]
    public async Task A_host_without_a_pz_image_is_listed_disabled_with_the_reason_and_not_preselected()
    {
        // #364: the Agent said its ZWARDEN_PZ_IMAGE is blank, so a deploy there could only fail.
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        AgentId ready = await SeedHostAsync(harness, "host-alpha", connected: true);
        AgentId noImage = await SeedHostAsync(harness, "host-bravo", connected: true);
        RecordPzImage(harness, noImage, ready: false);

        IRenderedComponent<DeployServerSheet> cut = await OpenAsync(harness);

        AngleSharp.Dom.IElement bravo = cut.Find($"#deploy-host option[value='{noImage}']");
        await Assert.That(bravo.TextContent).IsEqualTo("host-bravo — no PZ image configured");
        await Assert.That(bravo.HasAttribute("disabled")).IsTrue();
        await Assert.That(cut.Find("[data-host-no-image]").TextContent).Contains("ZWARDEN_PZ_IMAGE");
        // The one host that can take a server is preselected.
        await Assert.That(cut.FindComponents<BlazorBlueprint.Components.BbNativeSelect<string>>().Single().Instance.Value)
            .IsEqualTo(ready.ToString());
    }

    [Test]
    public async Task A_host_that_loses_its_pz_image_after_the_sheet_opened_is_refused_with_the_reason()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        AgentId agent = await SeedHostAsync(harness, "host-alpha", connected: true);
        IRenderedComponent<DeployServerSheet> cut = await ToGameVersionAsync(harness, "late");
        await NextAsync(cut, "memory");
        RecordPzImage(harness, agent, ready: false);

        await cut.Find("[data-action=step-finish]").ClickAsync(new());

        cut.WaitForState(() => cut.FindAll("[data-deploy-message]").Count == 1);
        await Assert.That(cut.Find("[data-deploy-message]").TextContent).Contains("Set ZWARDEN_PZ_IMAGE");
        await Assert.That(await ServerCountAsync(harness)).IsEqualTo(0);
    }

    [Test]
    public async Task A_hostile_host_label_is_rendered_escaped()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        await SeedHostAsync(harness, "<script>alert(1)</script>", connected: true);

        IRenderedComponent<DeployServerSheet> cut = await OpenAsync(harness);

        await Assert.That(cut.Markup).Contains("&lt;script&gt;");
        await Assert.That(cut.Markup).DoesNotContain("<script>alert(1)");
    }

    // --- #339: the Adopt existing tab ---------------------------------------------------------------------------

    [Test]
    public async Task With_nothing_discovered_the_sheet_has_no_adopt_tab()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        AgentId agent = await SeedHostAsync(harness, "host-alpha", connected: true);
        harness.Factory.Services.GetRequiredService<IServerDiscoveryCache>().Record(agent, []);

        IRenderedComponent<DeployServerSheet> cut = await OpenAsync(harness);

        await Assert.That(cut.FindAll("[data-deploy-tabs]")).IsEmpty();
    }

    [Test]
    public async Task The_banner_opens_the_sheet_on_adopt_existing_listing_host_and_state_without_ids()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        AgentId agent = await SeedHostAsync(harness, "host-alpha", connected: true);
        ServerId orphan = ServerId.New();
        Discover(harness, agent, (orphan, ServerRunState.Running));
        IRenderedComponent<DeployServerSheet> sheet = harness.Context.Render<DeployServerSheet>();
        IRenderedComponent<UnmanagedBanner> banner = harness.Context.Render<UnmanagedBanner>(
            p => p.Add(c => c.Hosts, [new UnmanagedHost("host-alpha", 1)]));
        await Assert.That(banner.Find("[data-unmanaged-text]").TextContent).IsEqualTo("1 unmanaged server found on host-alpha.");

        await banner.Find("[data-action=adopt-open]").ClickAsync(new());

        sheet.WaitForState(() => sheet.FindAll("[data-deploy-step=adopt]").Count == 1);
        await Assert.That(sheet.FindAll("[data-deploy-tabs]").Count).IsEqualTo(1);
        await Assert.That(sheet.FindAll("[data-step-strip]")).IsEmpty();
        string option = sheet.Find($"[data-adopt-option='{agent}|{orphan}']").TextContent;
        await Assert.That(option).Contains("Running");
        await Assert.That(option).Contains("host-alpha");
        await Assert.That(option).DoesNotContain(orphan.ToString());
        await Assert.That(sheet.Find("[data-action=step-finish]").TextContent.Trim()).IsEqualTo("Adopt");
    }

    [Test]
    public async Task Several_containers_on_one_host_are_told_apart_by_a_short_id()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        AgentId agent = await SeedHostAsync(harness, "host-alpha", connected: true);
        ServerId one = ServerId.New();
        ServerId two = ServerId.New();
        Discover(harness, agent, (one, ServerRunState.Running), (two, ServerRunState.Stopped));

        IRenderedComponent<DeployServerSheet> cut = await OpenAdoptAsync(harness);

        string text = cut.Find($"[data-adopt-option='{agent}|{two}']").TextContent;
        await Assert.That(text).Contains(two.ToString()[..12]);
        await Assert.That(text).DoesNotContain(two.ToString());
    }

    [Test]
    public async Task Adopt_needs_a_name()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        AgentId agent = await SeedHostAsync(harness, "host-alpha", connected: true);
        Discover(harness, agent, (ServerId.New(), ServerRunState.Running));
        IRenderedComponent<DeployServerSheet> cut = await OpenAdoptAsync(harness);

        await cut.Find("[data-action=step-finish]").ClickAsync(new());

        cut.WaitForState(() => cut.FindAll("[data-deploy-message]").Count == 1);
        await Assert.That(cut.Find("[data-deploy-message]").TextContent).Contains("Choose a container and a name.");
        await Assert.That(await ServerCountAsync(harness)).IsEqualTo(0);
    }

    [Test]
    public async Task Adopt_brings_the_chosen_container_under_management_and_toasts()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        AgentId agent = await SeedHostAsync(harness, "host-alpha", connected: true);
        ServerId first = ServerId.New();
        ServerId second = ServerId.New();
        Discover(harness, agent, (first, ServerRunState.Running), (second, ServerRunState.Stopped));
        IRenderedComponent<DeployServerSheet> cut = await OpenAdoptAsync(harness);
        await cut.Find($"[data-adopt-option='{agent}|{second}'] [role=radio]").ClickAsync(new());
        await InteractivePageHarness.TypeAsync(cut, "adopt-name", "restored");

        await cut.Find("[data-action=step-finish]").ClickAsync(new());

        cut.WaitForState(() => cut.FindAll("[data-deploy-server-sheet]").Count == 0);
        await using (AsyncServiceScope scope = harness.Factory.Services.CreateSystemScope())
        {
            Server adopted = await scope.ServiceProvider.GetRequiredService<ZWardenDbContext>().Set<Server>().SingleAsync();
            await Assert.That(adopted.Id).IsEqualTo(second);
            await Assert.That(adopted.Name).IsEqualTo("restored");
        }

        await Assert.That(cut.Markup).Contains("restored on host-alpha is now managed.");
        BunitNavigationManager nav = harness.Context.Services.GetRequiredService<BunitNavigationManager>();
        await Assert.That(nav.History.First().Options.ReplaceHistoryEntry).IsTrue();
    }

    [Test]
    public async Task The_adopt_deep_link_opens_on_adopt_existing_and_adopting_drops_it()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        AgentId agent = await SeedHostAsync(harness, "host-alpha", connected: true);
        Discover(harness, agent, (ServerId.New(), ServerRunState.Running));
        BunitNavigationManager nav = harness.Context.Services.GetRequiredService<BunitNavigationManager>();
        nav.NavigateTo("/servers?adopt=1");

        IRenderedComponent<DeployServerSheet> cut = harness.Context.Render<DeployServerSheet>(p => p
            .Add(c => c.OpenOnLoad, true)
            .Add(c => c.StartOnAdopt, true));

        cut.WaitForState(() => cut.FindAll("[data-deploy-step=adopt]").Count == 1);
        await InteractivePageHarness.TypeAsync(cut, "adopt-name", "linked");
        await cut.Find("[data-action=step-finish]").ClickAsync(new());
        cut.WaitForState(() => cut.FindAll("[data-deploy-server-sheet]").Count == 0);
        await Assert.That(nav.Uri).EndsWith("/servers");
    }

    [Test]
    public async Task The_tabs_switch_between_new_server_and_adopt_existing()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        AgentId agent = await SeedHostAsync(harness, "host-alpha", connected: true);
        Discover(harness, agent, (ServerId.New(), ServerRunState.Running));
        IRenderedComponent<DeployServerSheet> cut = await OpenAsync(harness);

        await cut.Find("[data-tab=adopt]").ClickAsync(new());
        cut.WaitForState(() => cut.FindAll("[data-deploy-step=adopt]").Count == 1);
        await cut.Find("[data-tab=new]").ClickAsync(new());

        cut.WaitForState(() => cut.FindAll("[data-deploy-step=basics]").Count == 1);
    }

    private static async Task<IRenderedComponent<DeployServerSheet>> OpenAdoptAsync(InteractivePageHarness harness)
    {
        IRenderedComponent<DeployServerSheet> cut = await OpenAsync(harness);
        await cut.Find("[data-tab=adopt]").ClickAsync(new());
        cut.WaitForState(() => cut.FindAll("[data-deploy-step=adopt]").Count == 1);
        return cut;
    }

    [Test]
    public async Task The_default_deploy_host_is_preselected_among_several_and_can_still_be_changed()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        AgentId alpha = await SeedHostAsync(harness, "host-alpha", connected: true);
        AgentId bravo = await SeedHostAsync(harness, "host-bravo", connected: true);
        await SetDefaultHostAsync(harness, bravo);

        IRenderedComponent<DeployServerSheet> cut = await OpenAsync(harness);
        BlazorBlueprint.Components.BbNativeSelect<string> picker = cut.FindComponents<BlazorBlueprint.Components.BbNativeSelect<string>>().Single().Instance;
        await Assert.That(picker.Value).IsEqualTo(bravo.ToString()); // #347

        await cut.Find("#deploy-host").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = alpha.ToString() });
        await Assert.That(cut.FindComponents<BlazorBlueprint.Components.BbNativeSelect<string>>().Single().Instance.Value)
            .IsEqualTo(alpha.ToString());
    }

    [Test]
    public async Task An_offline_default_deploy_host_is_not_preselected()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        AgentId alpha = await SeedHostAsync(harness, "host-alpha", connected: true);
        AgentId zulu = await SeedHostAsync(harness, "host-zulu", connected: false);
        await SetDefaultHostAsync(harness, zulu);

        IRenderedComponent<DeployServerSheet> cut = await OpenAsync(harness);

        // The default can't take a server now, so the existing rule applies: the one deployable host.
        await Assert.That(cut.FindComponents<BlazorBlueprint.Components.BbNativeSelect<string>>().Single().Instance.Value)
            .IsEqualTo(alpha.ToString());
    }

    // Saves the default deploy host through the real settings service as the harness's Owner.
    private static async Task SetDefaultHostAsync(InteractivePageHarness harness, AgentId host)
    {
        await using AsyncServiceScope scope = harness.Factory.Services.CreateSystemScope();
        Microsoft.AspNetCore.Identity.UserManager<ZWarden.Infrastructure.Identity.ApplicationUser> users =
            scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ZWarden.Infrastructure.Identity.ApplicationUser>>();
        ZWarden.Infrastructure.Identity.ApplicationUser owner = await users.FindByEmailAsync(InteractivePageHarness.OperatorEmail)
            ?? throw new InvalidOperationException("No harness operator.");
        await scope.ServiceProvider.GetRequiredService<ZWarden.Application.Settings.IControlPlaneSettingsService>()
            .SetDefaultDeployHostAsync(UserId.FromGuid(owner.Id), host);
    }

    private static void Discover(InteractivePageHarness harness, AgentId agent, params (ServerId Id, ServerRunState State)[] containers) =>
        harness.Factory.Services.GetRequiredService<IServerDiscoveryCache>()
            .Record(agent, [.. containers.Select(c => new DiscoveredServer(c.Id, c.State))]);

    private static async Task<IRenderedComponent<DeployServerSheet>> OpenAsync(InteractivePageHarness harness)
    {
        IRenderedComponent<DeployServerSheet> cut = harness.Context.Render<DeployServerSheet>();
        await cut.Find("[data-action=deploy-server-open]").ClickAsync(new());
        cut.WaitForState(() => cut.FindAll("[data-deploy-step=basics]").Count == 1);
        return cut;
    }

    // Opens the sheet with one connected host (preselected) and moves past Basics with only a name.
    private static async Task<IRenderedComponent<DeployServerSheet>> ToGameVersionAsync(InteractivePageHarness harness, string name)
    {
        if (!await HasHostAsync(harness))
        {
            await SeedHostAsync(harness, "host-alpha", connected: true);
        }

        IRenderedComponent<DeployServerSheet> cut = await OpenAsync(harness);
        await InteractivePageHarness.TypeAsync(cut, "deploy-name", name);
        await NextAsync(cut, "game-version");
        return cut;
    }

    private static async Task NextAsync(IRenderedComponent<DeployServerSheet> cut, string expectedStep)
    {
        await cut.Find("[data-action=step-next]").ClickAsync(new());
        cut.WaitForState(() => cut.FindAll($"[data-deploy-step={expectedStep}]").Count == 1);
    }

    private static async Task<Operation> WaitForProvisionAsync(InteractivePageHarness harness, IRenderedComponent<DeployServerSheet> cut)
    {
        Operation? provision = null;
        cut.WaitForState(() =>
        {
            using AsyncServiceScope scope = harness.Factory.Services.CreateSystemScope();
            provision = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>()
                .Set<Operation>().FirstOrDefault(o => o.Kind == OperationKind.ProvisionServer);
            return provision is not null;
        });
        await Task.CompletedTask;
        return provision!;
    }

    private static async Task<int> ServerCountAsync(InteractivePageHarness harness)
    {
        await using AsyncServiceScope scope = harness.Factory.Services.CreateSystemScope();
        return await scope.ServiceProvider.GetRequiredService<ZWardenDbContext>().Set<Server>().CountAsync();
    }

    private static async Task<bool> HasHostAsync(InteractivePageHarness harness)
    {
        await using AsyncServiceScope scope = harness.Factory.Services.CreateSystemScope();
        return await scope.ServiceProvider.GetRequiredService<ZWardenDbContext>().Set<Agent>().AnyAsync();
    }

    private static async Task<AgentId> SeedHostAsync(InteractivePageHarness harness, string label, bool connected)
    {
        await using AsyncServiceScope scope = harness.Factory.Services.CreateSystemScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Agent agent = Agent.Enroll(AgentHash, EnrollmentId.New(), DateTimeOffset.UtcNow, label);
        db.Set<Agent>().Add(agent);
        await db.SaveChangesAsync();
        if (connected)
        {
            harness.Factory.Services.GetRequiredService<IAgentConnectionRegistry>().Register(agent.Id, Guid.NewGuid().ToString(), () => { });
        }

        return agent.Id;
    }

    private static void RecordCapacity(InteractivePageHarness harness, AgentId agent, HostCapacity capacity) =>
        harness.Factory.Services.GetRequiredService<IHostCapacityCache>().Record(capacity with { AgentId = agent });

    private static void RecordPzImage(InteractivePageHarness harness, AgentId agent, bool ready) =>
        harness.Factory.Services.GetRequiredService<IHostProvisioningCache>().Record(agent, ready);
}
