using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Agents;
using ZWarden.Domain.Agents;
using ZWarden.Domain.Enrollments;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Web.Components.Pages.Hosts;
using ZWarden.Web.Tests.Account;

namespace ZWarden.Web.Tests.Hosts;

/// <summary>
/// F35/#159: the <c>/hosts</c> Host inventory page, re-skinned into the Signal card grid. It is gated tenant-wide
/// by the <c>Agent.View</c> policy (enforcement, not UI visibility — PRD 12) and renders each enrolled Agent as a
/// Host card with its self-reported facts (hostname, agent version, OS), observed connection state (Online /
/// Unreachable), and the Servers running on it. Exercised over the real host.
/// </summary>
public sealed class HostInventoryPageTests
{
    private const string StrongPassword = "correct horse battery staple";
    private const string AgentHash = "agenthashaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Test]
    public async Task The_page_is_gated_by_the_agent_view_permission_server_side()
    {
        AuthorizeAttribute attribute = typeof(HostInventory)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Single();

        await Assert.That(attribute.Policy).IsEqualTo("Agent.View");
    }

    [Test]
    public async Task Anonymous_is_redirected_from_the_inventory()
    {
        await using ZWardenWebAppFactory factory = new();
        using HttpClient client = factory.CreateWebClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/hosts", UriKind.Relative));

        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task An_operator_sees_the_inventory_with_the_empty_state()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);

        HttpResponseMessage response = await client.GetAsync(new Uri("/hosts", UriKind.Relative));
        string html = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(html).Contains("Hosts");
        await Assert.That(html).Contains("data-hosts-empty");
        client.Dispose();
    }

    [Test]
    public async Task The_page_offers_an_enroll_host_entry()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);

        string html = await (await client.GetAsync(new Uri("/hosts", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-hosts-enroll");
        await Assert.That(html).Contains("/enrollment");
        client.Dispose();
    }

    [Test]
    public async Task An_enrolled_agent_appears_as_a_host_card_with_its_reported_facts()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        await SeedHostAsync(factory);

        string html = await (await client.GetAsync(new Uri("/hosts", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-host-card");
        await Assert.That(html).Contains("pz-remote-1");   // self-reported hostname
        await Assert.That(html).Contains("1.4.2");         // self-reported agent version
        await Assert.That(html).Contains("Linux");         // self-reported OS platform
        client.Dispose();
    }

    [Test]
    public async Task A_host_with_no_live_connection_renders_the_degraded_unreachable_state()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        // Seeded but not registered in the live F10 registry, so it is not connected to this process.
        await SeedHostAsync(factory);

        string html = await (await client.GetAsync(new Uri("/hosts", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-host-degraded");
        await Assert.That(html).Contains("Unreachable");
        await Assert.That(html).Contains("No telemetry");
        client.Dispose();
    }

    [Test]
    public async Task A_host_connected_to_this_process_renders_as_online()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        AgentId agentId = await SeedHostAsync(factory);
        // Overlay a live connection on this process — the registry is authoritative for "connected now".
        factory.Services.GetRequiredService<IAgentConnectionRegistry>()
            .Register(agentId, "conn-online", () => { });

        string html = await (await client.GetAsync(new Uri("/hosts", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("Online");
        client.Dispose();
    }

    [Test]
    public async Task The_servers_running_on_a_host_link_to_their_detail_page()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        AgentId agentId = await SeedHostAsync(factory);
        ServerId serverId = await SeedServerOnAsync(factory, agentId, "camp-alpha");

        string html = await (await client.GetAsync(new Uri("/hosts", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-host-server");
        await Assert.That(html).Contains("camp-alpha");
        await Assert.That(html).Contains($"/servers/{serverId}");
        client.Dispose();
    }

    private static async Task<AgentId> SeedHostAsync(ZWardenWebAppFactory factory)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Agent agent = Agent.Enroll(AgentHash, EnrollmentId.New(), DateTimeOffset.UtcNow, "remote-alpha");
        agent.MarkConnected(protocolVersion: 1, DateTimeOffset.UtcNow);
        agent.RecordHostDescriptor("pz-remote-1", "1.4.2", "Linux");
        db.Set<Agent>().Add(agent);
        await db.SaveChangesAsync();
        return agent.Id;
    }

    private static async Task<ServerId> SeedServerOnAsync(ZWardenWebAppFactory factory, AgentId agentId, string name)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Server server = Server.Import(agentId, ServerId.New(), name, DateTimeOffset.UtcNow);
        db.Set<Server>().Add(server);
        await db.SaveChangesAsync();
        return server.Id;
    }

    private static async Task<HttpClient> SignedInOperatorAsync(ZWardenWebAppFactory factory)
    {
        await factory.CreateConfirmedUserAsync("op@zwarden.test", StrongPassword);
        await AuthorizationBootstrapper.EnsureSeededAsync(factory.Services, "op@zwarden.test");
        HttpClient client = factory.CreateWebClient();
        await LoginAsync(client, "op@zwarden.test", StrongPassword);
        return client;
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
