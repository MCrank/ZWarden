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
/// F14 S6: the <c>/servers</c> inventory dashboard. It is authenticated (not gated by a server-scoped
/// Server.View policy, which would deny at the page level), renders on the static server, and shows the
/// Servers the caller may view. Exercised over the real host.
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
    public async Task An_operator_sees_the_inventory_with_the_empty_state()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);

        HttpResponseMessage response = await client.GetAsync(new Uri("/servers", UriKind.Relative));
        string html = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(html).Contains("Servers");
        await Assert.That(html).Contains("data-servers-empty");
        client.Dispose();
    }

    [Test]
    public async Task An_imported_server_appears_on_the_dashboard()
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
        await Assert.That(html).Contains("data-server-row");
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

        // The adopted server (named from the form) now shows on the dashboard — the POST bound end to end.
        string after = await (await client.GetAsync(new Uri("/servers", UriKind.Relative))).Content.ReadAsStringAsync();
        await Assert.That(after).Contains("via-form");
        await Assert.That(after).Contains("data-server-row");
        client.Dispose();
    }

    [Test]
    public async Task The_dashboard_shows_lifecycle_actions_for_a_permitted_operator()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        await SeedServerAsync(factory, "controllable");

        string html = await (await client.GetAsync(new Uri("/servers", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("controllable");
        await Assert.That(html).Contains("data-lifecycle-actions");
        await Assert.That(html).Contains("data-action=\"start\"");
        await Assert.That(html).Contains("data-action=\"stop\"");
        await Assert.That(html).Contains("data-action=\"restart\"");
        await Assert.That(html).Contains("data-action=\"update\"");
        await Assert.That(html).Contains("data-server-build");
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
    public async Task The_fleet_table_links_each_row_to_its_detail_page()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "linked");

        string html = await (await client.GetAsync(new Uri("/servers", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains($"/servers/{serverId}");
        await Assert.That(html).Contains("data-server-health");
        client.Dispose();
    }

    [Test]
    public async Task The_lifecycle_form_posts_and_enqueues_an_operation()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "startable");

        string page = await (await client.GetAsync(new Uri("/servers", UriKind.Relative))).Content.ReadAsStringAsync();
        string token = ParseHiddenInputs(page)["__RequestVerificationToken"];

        // A submit button carries "{serverId}|{verb}" as the single bound Target — post it as the browser would.
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = token,
            ["_handler"] = "server-lifecycle",
            ["_lifecycleForm.Target"] = $"{serverId}|start",
        };
        HttpResponseMessage post = await client.PostAsync(new Uri("/servers", UriKind.Relative), new FormUrlEncodedContent(form));

        // The static POST bound end to end: a StartServer operation was enqueued for this Server.
        await Assert.That((int)post.StatusCode).IsLessThan(400);
        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        bool enqueued = db.Set<Domain.Operations.Operation>()
            .Any(o => o.ServerId == serverId && o.Kind == Domain.Operations.OperationKind.StartServer);
        await Assert.That(enqueued).IsTrue();
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
        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Server server = Server.Import(AgentId.New(), ServerId.New(), name, DateTimeOffset.UtcNow);
        db.Set<Server>().Add(server);
        await db.SaveChangesAsync();
        return server.Id;
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
