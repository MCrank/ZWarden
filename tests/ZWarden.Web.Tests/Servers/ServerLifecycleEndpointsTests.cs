using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Operations;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
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
    public async Task The_update_endpoint_enqueues_an_update_operation()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory);

        HttpResponseMessage response = await client.PostAsync(
            new Uri($"/api/servers/{serverId}/update", UriKind.Relative), content: null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);

        string op = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .GetProperty("operationId").GetString()!;
        string read = await (await client.GetAsync(new Uri($"/api/operations/{op}", UriKind.Relative)))
            .Content.ReadAsStringAsync();
        await Assert.That(read).Contains("UpdateServer");
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

    [Test]
    public async Task The_status_of_an_idle_running_server_follows_its_observed_state()
    {
        // #249: the live header polls this. Never cached, so a stale status can't linger.
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, ServerRunState.Running);

        HttpResponseMessage response = await client.GetAsync(new Uri($"/api/servers/{serverId}/status", UriKind.Relative));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(body.RootElement.GetProperty("label").GetString()).IsEqualTo("RUNNING");
        await Assert.That(body.RootElement.GetProperty("tone").GetString()).IsEqualTo("running");
        await Assert.That(body.RootElement.GetProperty("busy").GetBoolean()).IsFalse();
        await Assert.That(body.RootElement.GetProperty("canStart").GetBoolean()).IsFalse();
        await Assert.That(body.RootElement.GetProperty("canStop").GetBoolean()).IsTrue();
        await Assert.That(body.RootElement.GetProperty("canRestart").GetBoolean()).IsTrue();
        client.Dispose();
    }

    [Test]
    public async Task The_status_during_a_restart_says_restarting_and_disables_every_button()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, ServerRunState.Running);
        await client.PostAsync(new Uri($"/api/servers/{serverId}/restart", UriKind.Relative), content: null);

        // The Agent is offline, so the restart waits Pending — in flight, holding the lock.
        using JsonDocument body = JsonDocument.Parse(await (await client.GetAsync(
            new Uri($"/api/servers/{serverId}/status", UriKind.Relative))).Content.ReadAsStringAsync());

        await Assert.That(body.RootElement.GetProperty("label").GetString()).IsEqualTo("RESTARTING");
        await Assert.That(body.RootElement.GetProperty("tone").GetString()).IsEqualTo("busy");
        await Assert.That(body.RootElement.GetProperty("busy").GetBoolean()).IsTrue();
        await Assert.That(body.RootElement.GetProperty("canStop").GetBoolean()).IsFalse();
        await Assert.That(body.RootElement.GetProperty("canRestart").GetBoolean()).IsFalse();
        client.Dispose();
    }

    [Test]
    public async Task The_status_carries_the_in_flight_operations_progress_as_detail()
    {
        // #254: a header Restart's countdown shows what it is doing instead of a bare RESTARTING.
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, ServerRunState.Running);
        await client.PostAsync(new Uri($"/api/servers/{serverId}/restart", UriKind.Relative), content: null);
        await ReportProgressAsync(factory, serverId, "Restarting in 240 seconds.");

        using JsonDocument body = JsonDocument.Parse(await (await client.GetAsync(
            new Uri($"/api/servers/{serverId}/status", UriKind.Relative))).Content.ReadAsStringAsync());

        await Assert.That(body.RootElement.GetProperty("label").GetString()).IsEqualTo("RESTARTING");
        await Assert.That(body.RootElement.GetProperty("detail").GetString()).IsEqualTo("Restarting in 240 seconds.");
        client.Dispose();
    }

    // Drives the Server's in-flight Operation to Running and applies an Agent progress report, as the hub would.
    internal static async Task ReportProgressAsync(ZWardenWebAppFactory factory, ServerId serverId, string statusLine)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        IOperationStore store = scope.ServiceProvider.GetRequiredService<IOperationStore>();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Operation op = (await store.FindActiveForServerAsync(serverId))!;
        if (op.State == OperationState.Pending)
        {
            op.MarkDispatched(DateTimeOffset.UtcNow.AddMinutes(5), DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        await store.ApplyProgressAsync(op.Id, 0, statusLine);
    }

    [Test]
    public async Task The_status_of_an_unknown_server_is_not_found()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);

        HttpResponseMessage response = await client.GetAsync(new Uri($"/api/servers/{ServerId.New()}/status", UriKind.Relative));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        client.Dispose();
    }

    [Test]
    public async Task Anonymous_cannot_read_a_servers_status()
    {
        await using ZWardenWebAppFactory factory = new();
        ServerId serverId = await SeedServerAsync(factory, ServerRunState.Running);
        using HttpClient client = factory.CreateWebClient();

        HttpResponseMessage response = await client.GetAsync(new Uri($"/api/servers/{serverId}/status", UriKind.Relative));

        // Challenged (a redirect to login, possibly followed) — never the status itself.
        await Assert.That(await response.Content.ReadAsStringAsync()).DoesNotContain("\"canStop\"");
    }

    private static async Task<ServerId> SeedServerAsync(ZWardenWebAppFactory factory, ServerRunState? state = null)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Server server = Server.Import(AgentId.New(), ServerId.New(), "survivors", Now);
        if (state is { } observed)
        {
            server.RecordObservedState(observed, Now);
        }

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
