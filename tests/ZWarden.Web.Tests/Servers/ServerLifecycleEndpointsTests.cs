using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Web.Tests.Account;

namespace ZWarden.Web.Tests.Servers;

/// <summary>
/// F15 S4: the lifecycle API — <c>POST /api/servers/{id}/{start|stop|restart}</c>. Authenticated at the edge;
/// the service is the fail-closed server-scoped gate (the authz matrix is proven at the service tier). Each
/// verb enqueues a mutating, server-scoped Operation and returns 202 with the operation id; an unknown Server
/// is 404 and a malformed id is 400. Exercised over the real host; the seeded operator is Tenant Owner.
/// </summary>
public sealed class ServerLifecycleEndpointsTests
{
    private const string StrongPassword = "correct horse battery staple";
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Anonymous_cannot_start_a_server()
    {
        await using ZWardenWebAppFactory factory = new();
        using HttpClient client = factory.CreateWebClient();

        HttpResponseMessage response = await client.PostAsync(
            new Uri($"/api/servers/{ServerId.New()}/start", UriKind.Relative), content: null);

        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.Accepted);
    }

    [Test]
    public async Task An_operator_starts_a_registered_server()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory);

        HttpResponseMessage response = await client.PostAsync(
            new Uri($"/api/servers/{serverId}/start", UriKind.Relative), content: null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string operationId = body.RootElement.GetProperty("operationId").GetString()!;
        await Assert.That(operationId).StartsWith("op-");

        // The enqueued operation is a StartServer (the Agent is offline, so it waits Pending).
        string read = await (await client.GetAsync(new Uri($"/api/operations/{operationId}", UriKind.Relative)))
            .Content.ReadAsStringAsync();
        await Assert.That(read).Contains("StartServer");
        client.Dispose();
    }

    [Test]
    public async Task Stop_and_restart_endpoints_enqueue_their_operations()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId stopId = await SeedServerAsync(factory);
        ServerId restartId = await SeedServerAsync(factory);

        HttpResponseMessage stop = await client.PostAsync(
            new Uri($"/api/servers/{stopId}/stop", UriKind.Relative), content: null);
        HttpResponseMessage restart = await client.PostAsync(
            new Uri($"/api/servers/{restartId}/restart", UriKind.Relative), content: null);

        await Assert.That(stop.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That(restart.StatusCode).IsEqualTo(HttpStatusCode.Accepted);

        string stopOp = JsonDocument.Parse(await stop.Content.ReadAsStringAsync()).RootElement
            .GetProperty("operationId").GetString()!;
        string read = await (await client.GetAsync(new Uri($"/api/operations/{stopOp}", UriKind.Relative)))
            .Content.ReadAsStringAsync();
        await Assert.That(read).Contains("StopServer");
        client.Dispose();
    }

    [Test]
    public async Task An_unknown_server_is_not_found()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);

        HttpResponseMessage response = await client.PostAsync(
            new Uri($"/api/servers/{ServerId.New()}/start", UriKind.Relative), content: null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        client.Dispose();
    }

    [Test]
    public async Task A_malformed_id_is_a_bad_request()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);

        HttpResponseMessage response = await client.PostAsync(
            new Uri("/api/servers/not-a-server-id/start", UriKind.Relative), content: null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        client.Dispose();
    }

    private static async Task<ServerId> SeedServerAsync(ZWardenWebAppFactory factory)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Server server = Server.Import(AgentId.New(), ServerId.New(), "survivors", Now);
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
