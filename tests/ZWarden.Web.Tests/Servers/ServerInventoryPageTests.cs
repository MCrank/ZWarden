using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Servers;
using ZWarden.Domain.Agents;
using ZWarden.Domain.Enrollments;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Web.Components.Servers;
using ZWarden.Web.Tests.Account;

namespace ZWarden.Web.Tests.Servers;

/// <summary>
/// The <c>/servers</c> Fleet board (#158): the redesigned landing/fleet view. It is authenticated (not gated by
/// a server-scoped Server.View policy, which would deny at the page level), renders on the static server, and
/// self-filters to the Servers the caller may view (ADR 0018). The KPI strip, degraded banner and Register/
/// Import forms are static SSR; the fleet table is an interactive island (FleetBoard) that prerenders with the
/// rows. Exercised over the real host.
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

    [Test]
    public async Task The_import_form_posts_through_the_blueprint_components()
    {
        // The exemplar guarantee (issue #84): the Blueprint form primitives (BbNativeSelect/BbInput/BbButton)
        // render real named controls that bind on a static-SSR EditForm POST — no circuit.
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        AgentId agent = await SeedAgentAsync(factory);
        ServerId discovered = ServerId.New();
        factory.Services.GetRequiredService<IServerDiscoveryCache>()
            .Record(agent, [new DiscoveredServer(discovered, ServerRunState.Stopped)]);

        string page = await (await client.GetAsync(new Uri("/servers", UriKind.Relative))).Content.ReadAsStringAsync();
        string token = ParseHiddenInputs(page)["__RequestVerificationToken"];

        // The Blueprint controls must render the full model-path field names for the static POST to bind.
        // BbInput derives it; BbNativeSelect needs the explicit Name (its auto-derived name drops the prefix).
        await Assert.That(page).Contains("name=\"_form.Target\"");
        await Assert.That(page).Contains("name=\"_form.Name\"");

        // Submit the import EditForm exactly as the browser would (its rendered field names).
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = token,
            ["_handler"] = "import-server",
            ["_form.Target"] = $"{agent}|{discovered}",
            ["_form.Name"] = "via-form",
        };
        await client.PostAsync(new Uri("/servers", UriKind.Relative), new FormUrlEncodedContent(form));

        // The adopted server (named from the form) now shows on the board — the POST bound end to end.
        string after = await (await client.GetAsync(new Uri("/servers", UriKind.Relative))).Content.ReadAsStringAsync();
        await Assert.That(after).Contains("via-form");
        await Assert.That(after).Contains("data-server-link");
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
                now.AddHours(-5).AddMinutes(-2), "24909836"),
        ]);

        string html = await (await client.GetAsync(new Uri("/servers", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).DoesNotContain(">Tick<");
        await Assert.That(html).Contains($"data-fleet-server=\"{serverId}\"");
        await Assert.That(Regex.IsMatch(html, "data-fleet-cell=\"players\"[^>]*title=\"as of 3 min ago\"[^>]*>7<")).IsTrue();
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
    public async Task An_operator_sees_the_register_section()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);

        string html = await (await client.GetAsync(new Uri("/servers", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("Register a new server");
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
        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        AgentId agentId = AgentId.New();
        Server server = Server.Import(agentId, ServerId.New(), name, DateTimeOffset.UtcNow);
        db.Set<Server>().Add(server);
        await db.SaveChangesAsync();
        return (server.Id, agentId);
    }

    private static async Task<AgentId> SeedAgentAsync(ZWardenWebAppFactory factory)
    {
        using IServiceScope scope = factory.Services.CreateScope();
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
