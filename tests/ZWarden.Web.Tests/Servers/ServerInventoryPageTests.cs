using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Servers;
using ZWarden.Domain.Agents;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Enrollments;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Mods;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Identity;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Web.Components.Servers;
using ZWarden.Web.Tests.Account;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Web.Tests.Servers;

/// <summary>
/// The <c>/servers</c> Fleet board (#158): the redesigned landing/fleet view. It is authenticated (not gated by
/// a server-scoped Server.View policy, which would deny at the page level), renders on the static server, and
/// self-filters to the Servers the caller may view (ADR 0018). The KPI strip and degraded banner are static SSR; the
/// fleet table, the Deploy server sheet and the adopt callout are interactive islands that prerender with the page
/// (#338/#339). Exercised over the real host.
/// </summary>
public sealed class ServerInventoryPageTests
{
    private const string StrongPassword = "correct horse battery staple";
    private const string AgentHash = "agenthashaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Test]
    public async Task Anonymous_is_redirected_from_the_inventory()
    {
        await using ZWardenWebAppFactory factory = new();
        using HttpClient client = factory.CreateWebClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/servers", UriKind.Relative));

        // The client does not chase redirects, so an anonymous request is a redirect to login, never the page.
        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task An_operator_sees_the_fleet_board_with_the_empty_state()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);

        HttpResponseMessage response = await client.GetAsync(new Uri("/servers", UriKind.Relative));
        string html = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(html).Contains("Fleet");
        await Assert.That(html).Contains("data-fleet-board");
        // The interactive island prerenders its empty template server-side.
        await Assert.That(html).Contains("data-servers-empty");
        client.Dispose();
    }

    [Test]
    public async Task The_kpi_strip_shows_the_four_fleet_metrics()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);

        string html = await (await client.GetAsync(new Uri("/servers", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-kpi-strip");
        await Assert.That(html).Contains("data-kpi=\"running\"");
        await Assert.That(html).Contains("data-kpi=\"needs-attention\"");
        await Assert.That(html).Contains("data-kpi=\"players\"");
        await Assert.That(html).Contains("data-kpi=\"hosts\"");
        client.Dispose();
    }

    [Test]
    public async Task An_imported_server_appears_on_the_fleet_board()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        AgentId agent = await SeedAgentAsync(factory);
        ServerId discovered = ServerId.New();
        factory.Services.GetRequiredService<IServerDiscoveryCache>()
            .Record(agent, [new DiscoveredServer(discovered, ServerRunState.Running)]);

        HttpResponseMessage import = await client.PostAsJsonAsync(
            new Uri("/api/servers/import", UriKind.Relative),
            new ImportServerRequest(agent.ToString(), discovered.ToString(), "dashboard-shown"));
        await Assert.That(import.StatusCode).IsEqualTo(HttpStatusCode.OK);

        string html = await (await client.GetAsync(new Uri("/servers", UriKind.Relative))).Content.ReadAsStringAsync();
        await Assert.That(html).Contains("dashboard-shown");
        await Assert.That(html).Contains("data-server-link");
        client.Dispose();
    }

    // --- #339: the adopt callout replaces the inline Import form ------------------------------------------------

    [Test]
    public async Task With_nothing_discovered_there_is_no_adopt_banner_and_no_import_form()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        AgentId agent = await SeedAgentAsync(factory);
        factory.Services.GetRequiredService<IServerDiscoveryCache>().Record(agent, []);

        string html = await (await client.GetAsync(new Uri("/servers", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).DoesNotContain("data-unmanaged-banner");
        await Assert.That(html).DoesNotContain("Import a discovered container");
        await Assert.That(html).DoesNotContain("_form.Target");
        client.Dispose();
    }

    [Test]
    public async Task A_discovered_container_shows_the_adopt_banner_naming_its_host()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        AgentId agent = await SeedAgentAsync(factory);
        factory.Services.GetRequiredService<IServerDiscoveryCache>()
            .Record(agent, [new DiscoveredServer(ServerId.New(), ServerRunState.Running)]);

        string html = await (await client.GetAsync(new Uri("/servers", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-unmanaged-banner");
        await Assert.That(html).Contains("1 unmanaged server found on host-alpha.");
        await Assert.That(html).Contains("data-action=\"adopt-open\"");
        client.Dispose();
    }

    [Test]
    public async Task The_adopt_banner_counts_across_hosts_and_leaves_out_registered_and_foreign_containers()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        AgentId first = await SeedAgentAsync(factory);
        AgentId second = await SeedAgentAsync(factory);
        (ServerId registered, _) = await SeedServerWithAgentAsync(factory, "managed");
        IServerDiscoveryCache discovery = factory.Services.GetRequiredService<IServerDiscoveryCache>();
        discovery.Record(first, [new DiscoveredServer(ServerId.New(), ServerRunState.Running), new DiscoveredServer(registered, ServerRunState.Running)]);
        discovery.Record(second, [new DiscoveredServer(ServerId.New(), ServerRunState.Stopped)]);
        // An Agent this tenant doesn't own (another tenant's, in a hosted deployment) is never counted.
        discovery.Record(AgentId.New(), [new DiscoveredServer(ServerId.New(), ServerRunState.Stopped)]);

        string html = await (await client.GetAsync(new Uri("/servers", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("2 unmanaged servers found on 2 hosts.");
        client.Dispose();
    }

    [Test]
    public async Task A_caller_without_server_register_gets_no_adopt_banner()
    {
        await using ZWardenWebAppFactory factory = new();
        await factory.CreateConfirmedUserAsync("owner@zwarden.test", StrongPassword);
        await AuthorizationBootstrapper.EnsureSeededAsync(factory.Services, "owner@zwarden.test");
        await factory.CreateConfirmedUserAsync("mod@zwarden.test", StrongPassword);
        using (AsyncServiceScope scope = factory.Services.CreateSystemScope())
        {
            ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
            ApplicationUser user = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
                .FindByEmailAsync("mod@zwarden.test"))!;
            Role moderator = await db.Set<Role>().SingleAsync(r => r.BuiltIn == BuiltInRoleKind.Moderator);
            db.Set<RoleAssignment>().Add(RoleAssignment.TenantWide(moderator.TenantId, UserId.FromGuid(user.Id), moderator.Id));
            await db.SaveChangesAsync();
        }

        AgentId agent = await SeedAgentAsync(factory);
        factory.Services.GetRequiredService<IServerDiscoveryCache>()
            .Record(agent, [new DiscoveredServer(ServerId.New(), ServerRunState.Running)]);
        HttpClient client = factory.CreateWebClient();
        await LoginAsync(client, "mod@zwarden.test", StrongPassword);

        string html = await (await client.GetAsync(new Uri("/servers", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).DoesNotContain("data-unmanaged-banner");
        await Assert.That(html).DoesNotContain("data-unmanaged-live"); // #357: no island polling for it either
        client.Dispose();
    }

    [Test]
    public async Task The_detail_page_shows_the_server_and_embeds_the_live_panel()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "detail-me");

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();

        // The authorized static load rendered the server, and the interactive telemetry island prerendered with
        // its "awaiting" state (no sample cached yet).
        await Assert.That(html).Contains("detail-me");
        await Assert.That(html).Contains("data-live-panel");
        await Assert.That(html).Contains("data-live-awaiting");
        client.Dispose();
    }

    [Test]
    public async Task The_fleet_board_links_each_row_to_its_detail_page()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "linked");

        string html = await (await client.GetAsync(new Uri("/servers", UriKind.Relative))).Content.ReadAsStringAsync();

        // The Server cell is a real <a> (works without JS; URL is in the prerendered HTML), and whole-row click
        // navigates in the interactive island.
        await Assert.That(html).Contains($"/servers/{serverId}");
        await Assert.That(html).Contains("data-server-link");
        client.Dispose();
    }

    [Test]
    public async Task The_fleet_board_shows_an_in_flight_stop_and_is_marked_for_live_status()
    {
        // #253: each row's badge is rendered from the observed state AND the in-flight lifecycle Operation (so a
        // load mid-stop already says STOPPING), and the board names the batched endpoint live-status.js polls,
        // with every badge keyed by its Server id so the script can re-tone that row in place.
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "stopping");
        ServerLifecycleEndpointsTests.MarkHostOnline(factory, serverId);
        await client.PostAsync(new Uri($"/api/servers/{serverId}/stop", UriKind.Relative), content: null);

        string html = await (await client.GetAsync(new Uri("/servers", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-live-fleet=\"/api/servers/status\"");
        await Assert.That(html).Contains("data-status-busy=\"true\"");
        await Assert.That(html).Contains($"data-live-row=\"{serverId}\"");
        await Assert.That(html).Contains("STOPPING");
        client.Dispose();
    }

    [Test]
    public async Task The_fleet_board_shows_a_cpu_and_memory_meter_from_the_cached_sample()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);

        // Seed a Server and a live metrics sample for it, keyed by its true owning Agent (the cache's
        // ownership guard). The static load reads the snapshot and the island prerenders a MeterBar.
        (ServerId serverId, AgentId agentId) = await SeedServerWithAgentAsync(factory, "metered");
        factory.Services.GetRequiredService<IServerMetricsCache>().Record(
        [
            new ServerMetrics(agentId, serverId, CpuPercent: 42, MemoryUsedBytes: 512L * 1024 * 1024,
                MemoryLimitBytes: 1024L * 1024 * 1024, DiskUsedBytes: null, DiskCapacityBytes: null,
                PlayerCount: null, SampledAt: DateTimeOffset.UtcNow),
        ]);

        string html = await (await client.GetAsync(new Uri("/servers", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("metered");
        await Assert.That(html).Contains("role=\"meter\"");
        client.Dispose();
    }

    [Test]
    public async Task The_fleet_board_renders_players_uptime_and_version_and_drops_tick()
    {
        // #257: first render of the fleet facts; live-status.js keeps them (and the tiles) current from the poll.
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        (ServerId serverId, AgentId agentId) = await SeedServerWithAgentAsync(factory, "populated");
        factory.Services.GetRequiredService<Application.Agents.IAgentConnectionRegistry>()
            .Register(agentId, "conn-257", () => { });
        DateTimeOffset now = DateTimeOffset.UtcNow;
        factory.Services.GetRequiredService<IServerMetricsCache>().Record(
        [
            new ServerMetrics(agentId, serverId, 10, 1, 2, null, null, 7, now, now.AddMinutes(-3),
                now.AddHours(-5).AddMinutes(-2), "24909836", MaxPlayers: 16),
        ]);

        string html = await (await client.GetAsync(new Uri("/servers", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).DoesNotContain(">Tick<");
        await Assert.That(html).Contains($"data-fleet-server=\"{serverId}\"");
        await Assert.That(Regex.IsMatch(html, "data-fleet-cell=\"players\"[^>]*title=\"as of 3 min ago\"[^>]*>7 / 16<")).IsTrue();
        await Assert.That(Regex.IsMatch(html, "data-fleet-cell=\"uptime\"[^>]*>5h 2m<")).IsTrue();
        await Assert.That(Regex.IsMatch(html, "data-fleet-cell=\"version\"[^>]*>24909836<")).IsTrue();
        // The Players online tile sums the known counts.
        await Assert.That(Regex.IsMatch(html, "data-kpi=\"players\"[\\s\\S]*?data-kpi-value[^>]*>7<")).IsTrue();
        client.Dispose();
    }

    [Test]
    public async Task The_degraded_banner_shows_when_a_visible_servers_agent_is_unreachable()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        // A seeded Server's owning Agent has no live connection in the registry, so the fleet is degraded.
        await SeedServerAsync(factory, "orphaned");

        string html = await (await client.GetAsync(new Uri("/servers", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-degraded-banner");
        client.Dispose();
    }

    [Test]
    public async Task The_fleet_row_and_the_banner_name_the_host_by_its_reported_hostname()
    {
        // #336: the Host's name, not agt-…, under the server name and in the one-host unreachable banner.
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        await SeedServerOnHostAsync(factory, "named", label: null, hostname: "nsfw-01");

        string html = await (await client.GetAsync(new Uri("/servers", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(Regex.IsMatch(html, "data-fleet-host[^>]*>nsfw-01<")).IsTrue();
        await Assert.That(Regex.IsMatch(html, "data-degraded-banner[\\s\\S]*?>nsfw-01<")).IsTrue();
        client.Dispose();
    }

    [Test]
    public async Task The_operators_label_wins_over_the_reported_hostname()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        await SeedServerOnHostAsync(factory, "labelled", label: "Basement box", hostname: "nsfw-01");

        string html = await (await client.GetAsync(new Uri("/servers", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(Regex.IsMatch(html, "data-fleet-host[^>]*>Basement box<")).IsTrue();
        client.Dispose();
    }

    [Test]
    public async Task A_host_with_no_name_yet_shows_its_short_id()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        AgentId agent = await SeedServerOnHostAsync(factory, "nameless", label: null, hostname: null);

        string html = await (await client.GetAsync(new Uri("/servers", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(Regex.IsMatch(html, $"data-fleet-host[^>]*>{Regex.Escape(HtmlEncoder.Default.Encode(HostNames.ShortId(agent)))}<")).IsTrue();
        // The full id may key the row (the caller may view Hosts) but is never the shown name.
        await Assert.That(html).DoesNotContain($">{agent}<");
        client.Dispose();
    }

    [Test]
    public async Task A_caller_without_agent_view_sees_the_short_id_not_the_hostname()
    {
        // #336 D3: no new authorization — a Moderator may view the Server but not its Host, so no hostname.
        await using ZWardenWebAppFactory factory = new();
        await factory.CreateConfirmedUserAsync("owner@zwarden.test", StrongPassword);
        await AuthorizationBootstrapper.EnsureSeededAsync(factory.Services, "owner@zwarden.test");
        await factory.CreateConfirmedUserAsync("mod@zwarden.test", StrongPassword);
        using (AsyncServiceScope scope = factory.Services.CreateSystemScope())
        {
            ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
            ApplicationUser user = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
                .FindByEmailAsync("mod@zwarden.test"))!;
            Role moderator = await db.Set<Role>().SingleAsync(r => r.BuiltIn == BuiltInRoleKind.Moderator);
            db.Set<RoleAssignment>().Add(RoleAssignment.TenantWide(moderator.TenantId, UserId.FromGuid(user.Id), moderator.Id));
            await db.SaveChangesAsync();
        }

        AgentId agent = await SeedServerOnHostAsync(factory, "moderated", label: null, hostname: "nsfw-01");
        HttpClient client = factory.CreateWebClient();
        await LoginAsync(client, "mod@zwarden.test", StrongPassword);

        string html = await (await client.GetAsync(new Uri("/servers", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("moderated");
        await Assert.That(html).DoesNotContain("nsfw-01");
        await Assert.That(Regex.IsMatch(html, $"data-fleet-host[^>]*>{Regex.Escape(HtmlEncoder.Default.Encode(HostNames.ShortId(agent)))}<")).IsTrue();
        // #338: no Server.Register, no Deploy server button (the service re-checks on submit anyway).
        await Assert.That(html).DoesNotContain("data-action=\"deploy-server-open\"");
        // #340: still grouped under the Host, keyed by its short id — never the full AgentId — and no Host telemetry
        // without Agent.View.
        await Assert.That(html).Contains($"data-fleet-host-row=\"{HtmlEncoder.Default.Encode(HostNames.ShortId(agent))}\"");
        await Assert.That(html).DoesNotContain(agent.ToString());
        await Assert.That(html).DoesNotContain("data-live-hosts");
        await Assert.That(html).DoesNotContain("data-host-telemetry-for");
        client.Dispose();
    }

    // --- #357: the banners and the Hosts tile follow the Agents without a reload -----------------------------------

    [Test]
    public async Task With_every_host_connected_the_degraded_banner_is_in_the_page_but_hidden()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        AgentId agent = await SeedServerOnHostAsync(factory, "online", label: null, hostname: "nsfw-01");
        factory.Services.GetRequiredService<Application.Agents.IAgentConnectionRegistry>().Register(agent, "conn-357", () => { });

        string html = await (await client.GetAsync(new Uri("/servers", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(Regex.IsMatch(html, "data-degraded-banner hidden")).IsTrue();
        await Assert.That(html).Contains("data-live-fleet-hosts=\"/api/fleet/hosts\"");
        await Assert.That(Regex.IsMatch(html, "data-kpi=\"hosts\"[\\s\\S]*?data-kpi-sub>all connected<")).IsTrue();
        client.Dispose();
    }

    [Test]
    public async Task Several_unreachable_hosts_are_counted_and_named_in_the_title()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        await SeedServerOnHostAsync(factory, "one", label: null, hostname: "nsfw-01");
        await SeedServerOnHostAsync(factory, "two", label: null, hostname: "nsfw-02");

        string html = await (await client.GetAsync(new Uri("/servers", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(Regex.IsMatch(html, "data-degraded-banner hidden")).IsFalse();
        await Assert.That(Regex.IsMatch(html, "data-degraded-one hidden")).IsTrue();
        await Assert.That(Regex.IsMatch(html, "data-degraded-many title=\"nsfw-0[12], nsfw-0[12]\"><span data-degraded-count>2<")).IsTrue();
        await Assert.That(Regex.IsMatch(html, "data-kpi=\"hosts\"[\\s\\S]*?data-kpi-sub>2 unreachable<")).IsTrue();
        client.Dispose();
    }

    [Test]
    public async Task A_register_caller_gets_the_adopt_island_even_with_nothing_to_adopt()
    {
        // The island re-reads the counts itself, so it must be on the page before there is anything to show.
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        await SeedAgentAsync(factory);

        string html = await (await client.GetAsync(new Uri("/servers", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-unmanaged-live");
        await Assert.That(html).DoesNotContain("data-unmanaged-banner");
        client.Dispose();
    }

    [Test]
    public async Task The_fleet_hosts_feed_reports_totals_and_names_the_unreachable_hosts()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        AgentId online = await SeedServerOnHostAsync(factory, "up", label: null, hostname: "nsfw-01");
        await SeedServerOnHostAsync(factory, "down", label: "Basement box", hostname: "nsfw-02");
        await SeedAgentAsync(factory); // a Host with no Servers yet, never connected (#342: it still counts)
        factory.Services.GetRequiredService<Application.Agents.IAgentConnectionRegistry>().Register(online, "conn-357", () => { });

        HttpResponseMessage response = await client.GetAsync(new Uri("/api/fleet/hosts", UriKind.Relative));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(body.RootElement.GetProperty("total").GetInt32()).IsEqualTo(3);
        await Assert.That(body.RootElement.GetProperty("online").GetInt32()).IsEqualTo(1);
        string[] unreachable = [.. body.RootElement.GetProperty("unreachable").EnumerateArray().Select(e => e.GetString()!)];
        await Assert.That(unreachable).Contains("Basement box");
        await Assert.That(unreachable).Contains("host-alpha");
        await Assert.That(unreachable.Length).IsEqualTo(2);
        client.Dispose();
    }

    [Test]
    public async Task The_fleet_hosts_feed_names_hosts_by_short_id_without_agent_view()
    {
        await using ZWardenWebAppFactory factory = new();
        await factory.CreateConfirmedUserAsync("owner@zwarden.test", StrongPassword);
        await AuthorizationBootstrapper.EnsureSeededAsync(factory.Services, "owner@zwarden.test");
        await factory.CreateConfirmedUserAsync("mod@zwarden.test", StrongPassword);
        using (AsyncServiceScope scope = factory.Services.CreateSystemScope())
        {
            ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
            ApplicationUser user = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
                .FindByEmailAsync("mod@zwarden.test"))!;
            Role moderator = await db.Set<Role>().SingleAsync(r => r.BuiltIn == BuiltInRoleKind.Moderator);
            db.Set<RoleAssignment>().Add(RoleAssignment.TenantWide(moderator.TenantId, UserId.FromGuid(user.Id), moderator.Id));
            await db.SaveChangesAsync();
        }

        AgentId agent = await SeedServerOnHostAsync(factory, "moderated", label: null, hostname: "nsfw-01");
        await SeedAgentAsync(factory); // no Servers: invisible without Agent.View
        HttpClient client = factory.CreateWebClient();
        await LoginAsync(client, "mod@zwarden.test", StrongPassword);

        string json = await (await client.GetAsync(new Uri("/api/fleet/hosts", UriKind.Relative))).Content.ReadAsStringAsync();

        using JsonDocument body = JsonDocument.Parse(json);
        await Assert.That(body.RootElement.GetProperty("total").GetInt32()).IsEqualTo(1);
        await Assert.That(body.RootElement.GetProperty("unreachable")[0].GetString()).IsEqualTo(HostNames.ShortId(agent));
        await Assert.That(json).DoesNotContain("nsfw-01");
        await Assert.That(json).DoesNotContain("host-alpha");
        client.Dispose();
    }

    [Test]
    public async Task The_fleet_hosts_feed_shows_nothing_to_a_caller_who_may_view_nothing()
    {
        await using ZWardenWebAppFactory factory = new();
        await SeedServerOnHostAsync(factory, "hidden", label: null, hostname: "nsfw-01");
        await factory.CreateConfirmedUserAsync("nobody@zwarden.test", StrongPassword);
        HttpClient client = factory.CreateWebClient();
        await LoginAsync(client, "nobody@zwarden.test", StrongPassword);

        string json = await (await client.GetAsync(new Uri("/api/fleet/hosts", UriKind.Relative))).Content.ReadAsStringAsync();

        using JsonDocument body = JsonDocument.Parse(json);
        await Assert.That(body.RootElement.GetProperty("total").GetInt32()).IsEqualTo(0);
        await Assert.That(body.RootElement.GetProperty("unreachable").GetArrayLength()).IsEqualTo(0);
        client.Dispose();
    }

    [Test]
    public async Task Anonymous_cannot_read_the_fleet_hosts_feed()
    {
        await using ZWardenWebAppFactory factory = new();
        await SeedServerOnHostAsync(factory, "hidden", label: null, hostname: "nsfw-01");
        using HttpClient client = factory.CreateWebClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/api/fleet/hosts", UriKind.Relative));

        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.OK);
        await Assert.That(await response.Content.ReadAsStringAsync()).DoesNotContain("nsfw-01");
    }

    // --- #340: the hierarchical grid, Servers under Host rollup rows ---------------------------------------------

    [Test]
    public async Task A_host_with_no_servers_still_gets_a_row_and_counts_as_a_host()
    {
        // #342 live pass: a freshly enrolled Host has no Servers yet, and the Fleet hid it (rows and counts came from
        // the Servers only).
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        AgentId busy = await SeedServerOnHostAsync(factory, "NSFW", label: "NSFW-1", hostname: null);
        AgentId empty = await SeedAgentAsync(factory);
        factory.Services.GetRequiredService<Application.Agents.IAgentConnectionRegistry>().Register(empty, "conn-empty", () => { });

        string html = await (await client.GetAsync(new Uri("/servers", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains($"data-fleet-host-row=\"{busy}\"");
        await Assert.That(html).Contains($"data-fleet-host-row=\"{empty}\"");
        await Assert.That(Regex.IsMatch(html, "data-fleet-host-count[^>]*>0 servers<")).IsTrue();
        await Assert.That(html).Contains("1 server · <span data-fleet-hosts-count>2 hosts</span>");
        await Assert.That(Regex.IsMatch(html, "data-kpi=\"hosts\"[\\s\\S]*?data-hosts-online>1<[\\s\\S]*?data-hosts-total>2<")).IsTrue();
        client.Dispose();
    }

    [Test]
    public async Task A_server_sits_under_its_host_row_with_the_count_and_the_unreachable_chip()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        AgentId agent = await SeedServerOnHostAsync(factory, "NSFW", label: null, hostname: "nsfw-01");

        string html = await (await client.GetAsync(new Uri("/servers", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains($"data-fleet-host-row=\"{agent}\"");
        await Assert.That(Regex.IsMatch(html, "data-fleet-host-count[^>]*>1 server<")).IsTrue();
        await Assert.That(Regex.IsMatch(html, "data-fleet-host-status[\\s\\S]*?zw-status-unknown")).IsTrue();
        // The Host row comes first, then its Server.
        await Assert.That(html.IndexOf($"data-fleet-host-row=\"{agent}\"", StringComparison.Ordinal))
            .IsLessThan(html.IndexOf(">NSFW<", StringComparison.Ordinal));
        // Unreachable: no telemetry meters (the feed only reports connected Hosts).
        await Assert.That(html).DoesNotContain("data-host-telemetry-for");
        // Expand all / Collapse all.
        await Assert.That(html).Contains("data-fleet-expand=\"all\"");
        await Assert.That(html).Contains("data-fleet-expand=\"none\"");
        client.Dispose();
    }

    [Test]
    public async Task A_connected_host_row_shows_its_cpu_and_memory_meters_from_the_telemetry_feed()
    {
        const long GiB = 1024L * 1024 * 1024;
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        AgentId agent = await SeedServerOnHostAsync(factory, "live", label: null, hostname: "nsfw-01");
        factory.Services.GetRequiredService<Application.Agents.IAgentConnectionRegistry>().Register(agent, "conn-online", () => { });
        factory.Services.GetRequiredService<IHostCapacityCache>().Record(new HostCapacity(
            agent, 32 * GiB, 0, 0, 4 * GiB, 0, DateTimeOffset.UtcNow,
            new HostVitals(37.5, 12 * GiB, 212 * GiB, 480 * GiB, 4, 0.12, 0.2, 0.18)));

        string html = await (await client.GetAsync(new Uri("/servers", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-live-hosts=\"/api/hosts/telemetry\"");
        await Assert.That(html).Contains($"data-host-telemetry-for=\"{agent}\"");
        await Assert.That(html).Contains("aria-label=\"Host CPU: 38%\"");
        await Assert.That(html).Contains("aria-label=\"Host memory: 38%\"");
        await Assert.That(Regex.IsMatch(html, "data-fleet-host-status[\\s\\S]*?zw-status-running")).IsTrue();
        client.Dispose();
    }

    [Test]
    public async Task A_host_row_sums_its_servers_players_and_names_its_members_for_the_poll()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        AgentId agent = await SeedServerOnHostAsync(factory, "rolled", label: null, hostname: "nsfw-01");
        ServerId serverId;
        using (AsyncServiceScope scope = factory.Services.CreateSystemScope())
        {
            serverId = (await scope.ServiceProvider.GetRequiredService<ZWardenDbContext>().Set<Server>().SingleAsync()).Id;
        }

        factory.Services.GetRequiredService<Application.Agents.IAgentConnectionRegistry>().Register(agent, "conn-online", () => { });
        DateTimeOffset now = DateTimeOffset.UtcNow;
        factory.Services.GetRequiredService<IServerMetricsCache>().Record(
        [
            new ServerMetrics(agent, serverId, 10, 1_000, 2_000, null, null, 3, now, now, null, null, MaxPlayers: 16),
        ]);

        string html = await (await client.GetAsync(new Uri("/servers", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(Regex.IsMatch(html, $"data-fleet-rollup=\"{agent}\" data-fleet-members=\"{serverId}\"[^>]*>3 / 16<")).IsTrue();
        client.Dispose();
    }

    [Test]
    public async Task A_server_on_an_unknown_agent_goes_in_the_unassigned_group()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        await SeedServerAsync(factory, "orphan"); // its Agent has no record

        string html = await (await client.GetAsync(new Uri("/servers", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-fleet-host-row=\"unassigned\"");
        await Assert.That(Regex.IsMatch(html, "data-fleet-host[^>]*>Unassigned<")).IsTrue();
        await Assert.That(html).Contains("orphan");
        client.Dispose();
    }

    // --- #338: the Deploy server sheet replaces the #230 inline form ---------------------------------------------

    [Test]
    public async Task An_operator_gets_the_deploy_server_button_and_no_inline_register_form()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        await SeedAgentAsync(factory);

        string html = await (await client.GetAsync(new Uri("/servers", UriKind.Relative))).Content.ReadAsStringAsync();

        // The island prerenders its button; the sheet itself opens only in the circuit.
        await Assert.That(html).Contains("data-deploy-server");
        await Assert.That(html).Contains("data-action=\"deploy-server-open\"");
        await Assert.That(html).DoesNotContain("Register a new server");
        await Assert.That(html).DoesNotContain("data-new-server-wizard");
        await Assert.That(html).DoesNotContain("_registerForm");
        client.Dispose();
    }

    [Test]
    public async Task The_deploy_deep_link_renders_the_fleet_page()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);

        HttpResponseMessage response = await client.GetAsync(new Uri("/servers?deploy=1", UriKind.Relative));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await response.Content.ReadAsStringAsync()).Contains("data-action=\"deploy-server-open\"");
        client.Dispose();
    }

    [Test]
    public async Task The_fleet_board_shows_each_servers_branch_as_plain_text_under_the_version()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        using (AsyncServiceScope scope = factory.Services.CreateSystemScope())
        {
            ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
            db.Set<Server>().Add(Server.Register(AgentId.New(), "on-public", DateTimeOffset.UtcNow));
            db.Set<Server>().Add(Server.Register(AgentId.New(), "on-unstable", DateTimeOffset.UtcNow, branch: "unstable"));
            await db.SaveChangesAsync();
        }

        string html = await (await client.GetAsync(new Uri("/servers", UriKind.Relative))).Content.ReadAsStringAsync();

        // Live pass: a plain muted line like the host under the name, not a pill; every row has one so rows line up.
        await Assert.That(Regex.Count(html, "data-fleet-branch")).IsEqualTo(2);
        await Assert.That(Regex.IsMatch(html, "<span[^>]*data-fleet-branch[^>]*>public<")).IsTrue();
        await Assert.That(Regex.IsMatch(html, "data-fleet-branch[^>]*>\\s*unstable \\(preview\\)")).IsTrue();
        client.Dispose();
    }
    [Test]
    public async Task The_fleet_board_hints_at_mod_updates_on_running_servers_only()
    {
        // #275 D5/D7: a running server's players are turned away until it restarts; a stopped one pulls them on start.
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        using (AsyncServiceScope scope = factory.Services.CreateSystemScope())
        {
            ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            foreach ((string name, ServerRunState state) in new[] { ("running", ServerRunState.Running), ("stopped", ServerRunState.Stopped) })
            {
                Server server = Server.Register(AgentId.New(), name, now);
                server.RecordObservedState(state, now);
                db.Set<Server>().Add(server);
                ServerModState mods = ServerModState.For(server.Id);
                mods.MarkBooted(now.AddHours(-2));
                mods.ObserveConfig(["100", "200"], ["A", "B"], now.AddHours(-2));
                db.Set<ServerModState>().Add(mods);
                foreach (string workshopId in new[] { "100", "200" })
                {
                    ServerWorkshopItem item = ServerWorkshopItem.Track(server.Id, workshopId);
                    item.ApplyMetadata("t", null, null, now.AddHours(-1), [], [], now);
                    item.ObserveDisk(onDisk: true, [], now, installedUpdatedAt: now.AddDays(workshopId == "100" ? -1 : 0));
                    db.Set<ServerWorkshopItem>().Add(item);
                }
            }

            await db.SaveChangesAsync();
        }

        string html = await (await client.GetAsync(new Uri("/servers", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(Regex.Count(html, "data-fleet-mod-updates")).IsEqualTo(1);
        await Assert.That(Regex.IsMatch(html, "data-fleet-mod-updates[^>]*>\\s*1 mod update<")).IsTrue();
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

    private static async Task<ServerId> SeedServerAsync(ZWardenWebAppFactory factory, string name)
    {
        (ServerId serverId, _) = await SeedServerWithAgentAsync(factory, name);
        return serverId;
    }

    private static async Task<(ServerId ServerId, AgentId AgentId)> SeedServerWithAgentAsync(ZWardenWebAppFactory factory, string name)
    {
        using AsyncServiceScope scope = factory.Services.CreateSystemScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        AgentId agentId = AgentId.New();
        Server server = Server.Import(agentId, ServerId.New(), name, DateTimeOffset.UtcNow);
        db.Set<Server>().Add(server);
        await db.SaveChangesAsync();
        return (server.Id, agentId);
    }

    private static async Task<AgentId> SeedServerOnHostAsync(
        ZWardenWebAppFactory factory, string name, string? label, string? hostname)
    {
        using AsyncServiceScope scope = factory.Services.CreateSystemScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Agent agent = Agent.Enroll(AgentHash, EnrollmentId.New(), DateTimeOffset.UtcNow, label);
        if (hostname is not null)
        {
            agent.RecordHostDescriptor(hostname, "1.0.0", "Linux");
        }

        db.Set<Agent>().Add(agent);
        db.Set<Server>().Add(Server.Import(agent.Id, ServerId.New(), name, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        return agent.Id;
    }

    private static async Task<AgentId> SeedAgentAsync(ZWardenWebAppFactory factory)
    {
        using AsyncServiceScope scope = factory.Services.CreateSystemScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Agent agent = Agent.Enroll(AgentHash, EnrollmentId.New(), DateTimeOffset.UtcNow, "host-alpha");
        db.Set<Agent>().Add(agent);
        await db.SaveChangesAsync();
        return agent.Id;
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
