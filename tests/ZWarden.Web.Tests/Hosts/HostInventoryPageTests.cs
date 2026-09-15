using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Domain.Agents;
using ZWarden.Domain.Enrollments;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Web.Components.Pages.Hosts;
using ZWarden.Web.Tests.Account;

namespace ZWarden.Web.Tests.Hosts;

/// <summary>
/// F35: the <c>/hosts</c> Host inventory page. It is gated tenant-wide by the <c>Agent.View</c> policy
/// (enforcement, not UI visibility — PRD 12) and renders each enrolled Agent as a Host with its self-reported
/// facts (hostname, agent version, OS) and observed connection state. Exercised over the real host.
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
    public async Task An_enrolled_agent_appears_as_a_host_with_its_reported_facts()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        await SeedHostAsync(factory);

        string html = await (await client.GetAsync(new Uri("/hosts", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-host-row");
        await Assert.That(html).Contains("pz-remote-1");   // self-reported hostname
        await Assert.That(html).Contains("1.4.2");         // self-reported agent version
        await Assert.That(html).Contains("Linux");         // self-reported OS platform
        client.Dispose();
    }

    private static async Task SeedHostAsync(ZWardenWebAppFactory factory)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Agent agent = Agent.Enroll(AgentHash, EnrollmentId.New(), DateTimeOffset.UtcNow, "remote-alpha");
        agent.MarkConnected(protocolVersion: 1, DateTimeOffset.UtcNow);
        agent.RecordHostDescriptor("pz-remote-1", "1.4.2", "Linux");
        db.Set<Agent>().Add(agent);
        await db.SaveChangesAsync();
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
