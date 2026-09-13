using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
/// F14 S5: the operator Server inventory + import API. The list is authenticated and self-filtering; import
/// and discovery are gated by <c>Server.Register</c> and validate the target against what the Agent actually
/// discovered. Exercised over the real host; the seeded admin is Tenant Owner (all permissions tenant-wide).
/// </summary>
public sealed class ServerEndpointsTests
{
    private const string StrongPassword = "correct horse battery staple";
    private const string AgentHash = "agenthashaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Test]
    public async Task Anonymous_cannot_list_servers()
    {
        await using ZWardenWebAppFactory factory = new();
        using HttpClient client = factory.CreateWebClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/api/servers", UriKind.Relative));

        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Anonymous_cannot_import_a_server()
    {
        await using ZWardenWebAppFactory factory = new();
        using HttpClient client = factory.CreateWebClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri("/api/servers/import", UriKind.Relative),
            new ImportServerRequest(AgentId.New().ToString(), ServerId.New().ToString(), "nope"));

        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task An_operator_sees_an_empty_inventory_initially()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);

        HttpResponseMessage response = await client.GetAsync(new Uri("/api/servers", UriKind.Relative));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(body.RootElement.GetArrayLength()).IsEqualTo(0);
        client.Dispose();
    }

    [Test]
    public async Task An_operator_imports_a_discovered_server_and_it_appears_and_is_idempotent()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        AgentId agent = await SeedAgentAsync(factory);
        ServerId discovered = ServerId.New();
        factory.Services.GetRequiredService<IServerDiscoveryCache>()
            .Record(agent, [new DiscoveredServer(discovered, ServerRunState.Stopped)]);

        HttpResponseMessage import = await client.PostAsJsonAsync(
            new Uri("/api/servers/import", UriKind.Relative),
            new ImportServerRequest(agent.ToString(), discovered.ToString(), "survivors-1"));
        await Assert.That(import.StatusCode).IsEqualTo(HttpStatusCode.OK);

        HttpResponseMessage list = await client.GetAsync(new Uri("/api/servers", UriKind.Relative));
        string listBody = await list.Content.ReadAsStringAsync();
        await Assert.That(listBody).Contains(discovered.ToString());
        await Assert.That(listBody).Contains("survivors-1");

        // Re-importing the same discovered id is idempotent — still exactly one Server.
        HttpResponseMessage again = await client.PostAsJsonAsync(
            new Uri("/api/servers/import", UriKind.Relative),
            new ImportServerRequest(agent.ToString(), discovered.ToString(), "survivors-1"));
        await Assert.That(again.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using JsonDocument after = JsonDocument.Parse(await (await client.GetAsync(
            new Uri("/api/servers", UriKind.Relative))).Content.ReadAsStringAsync());
        await Assert.That(after.RootElement.GetArrayLength()).IsEqualTo(1);
        client.Dispose();
    }

    [Test]
    public async Task Anonymous_cannot_register_a_server()
    {
        await using ZWardenWebAppFactory factory = new();
        using HttpClient client = factory.CreateWebClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri("/api/servers", UriKind.Relative),
            new RegisterServerRequest(AgentId.New().ToString(), "nope"));

        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.Accepted);
        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task An_operator_registers_a_server_and_it_appears_in_the_inventory()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        AgentId agent = await SeedAgentAsync(factory);

        HttpResponseMessage register = await client.PostAsJsonAsync(
            new Uri("/api/servers", UriKind.Relative),
            new RegisterServerRequest(agent.ToString(), "provisioned-1"));

        await Assert.That(register.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        using JsonDocument body = JsonDocument.Parse(await register.Content.ReadAsStringAsync());
        await Assert.That(body.RootElement.GetProperty("serverId").GetString()).StartsWith("srv-");
        await Assert.That(body.RootElement.GetProperty("operationId").GetString()).StartsWith("op-");

        string list = await (await client.GetAsync(new Uri("/api/servers", UriKind.Relative))).Content.ReadAsStringAsync();
        await Assert.That(list).Contains("provisioned-1");
        await Assert.That(list).Contains("Unknown");
        client.Dispose();
    }

    [Test]
    public async Task Registering_on_an_unknown_agent_is_not_found()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);

        HttpResponseMessage register = await client.PostAsJsonAsync(
            new Uri("/api/servers", UriKind.Relative),
            new RegisterServerRequest(AgentId.New().ToString(), "orphaned"));

        await Assert.That(register.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        client.Dispose();
    }

    [Test]
    public async Task Importing_an_undiscovered_id_is_not_found()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        AgentId agent = await SeedAgentAsync(factory);

        HttpResponseMessage import = await client.PostAsJsonAsync(
            new Uri("/api/servers/import", UriKind.Relative),
            new ImportServerRequest(agent.ToString(), ServerId.New().ToString(), "forged"));

        await Assert.That(import.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
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
