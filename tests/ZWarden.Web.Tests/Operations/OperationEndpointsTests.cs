using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Web.Tests.Account;

namespace ZWarden.Web.Tests.Operations;

/// <summary>
/// F11 PR-B: the minimal operations API is gated by <c>Agent.Manage</c> (F5) — anonymous callers are refused
/// — and an authorized operator can enqueue a <c>Diagnostics.Ping</c> and read the resulting Operation's
/// state. Exercised over the real host.
/// </summary>
public sealed class OperationEndpointsTests
{
    private const string StrongPassword = "correct horse battery staple";

    [Test]
    public async Task Anonymous_cannot_reach_the_operations_api()
    {
        await using ZWardenWebAppFactory factory = new();
        using HttpClient client = factory.CreateWebClient();

        HttpResponseMessage response = await client.PostAsync(
            new Uri($"/api/agents/{AgentId.New()}/ping", UriKind.Relative), content: null);

        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.Accepted);
        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task An_operator_enqueues_a_ping_and_reads_its_state()
    {
        await using ZWardenWebAppFactory factory = new();
        await factory.CreateConfirmedUserAsync("op@zwarden.test", StrongPassword);
        await AuthorizationBootstrapper.EnsureSeededAsync(factory.Services, "op@zwarden.test");
        using HttpClient client = factory.CreateWebClient();
        await LoginAsync(client, "op@zwarden.test", StrongPassword);

        HttpResponseMessage ping = await client.PostAsync(
            new Uri($"/api/agents/{AgentId.New()}/ping", UriKind.Relative), content: null);

        await Assert.That(ping.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        using JsonDocument body = JsonDocument.Parse(await ping.Content.ReadAsStringAsync());
        string operationId = body.RootElement.GetProperty("operationId").GetString()!;
        await Assert.That(operationId).StartsWith("op-");

        HttpResponseMessage read = await client.GetAsync(new Uri($"/api/operations/{operationId}", UriKind.Relative));
        await Assert.That(read.StatusCode).IsEqualTo(HttpStatusCode.OK);
        string readBody = await read.Content.ReadAsStringAsync();
        // No connected Agent, so the operation waits Pending.
        await Assert.That(readBody).Contains("Pending");
        await Assert.That(readBody).Contains("DiagnosticsPing");
    }

    [Test]
    public async Task Reading_an_unknown_operation_is_not_found()
    {
        await using ZWardenWebAppFactory factory = new();
        await factory.CreateConfirmedUserAsync("op@zwarden.test", StrongPassword);
        await AuthorizationBootstrapper.EnsureSeededAsync(factory.Services, "op@zwarden.test");
        using HttpClient client = factory.CreateWebClient();
        await LoginAsync(client, "op@zwarden.test", StrongPassword);

        HttpResponseMessage read = await client.GetAsync(new Uri($"/api/operations/{OperationId.New()}", UriKind.Relative));

        await Assert.That(read.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
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
