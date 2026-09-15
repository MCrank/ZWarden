using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Web.Tests.Account;

namespace ZWarden.Web.Tests.Diagnostics;

/// <summary>
/// F29 PR-A: the diagnostics API is gated by the tenant-wide <c>Diagnostics.View</c> (F5) — anonymous callers are
/// refused — and an authorized operator can run a read-only sweep and read back a report covering every domain,
/// with the Agent-gathered domains reported Skipped until PR-B/PR-C. Exercised over the real host.
/// </summary>
public sealed class DiagnosticsEndpointsTests
{
    private const string StrongPassword = "correct horse battery staple";

    [Test]
    public async Task Anonymous_cannot_run_diagnostics()
    {
        await using ZWardenWebAppFactory factory = new();
        using HttpClient client = factory.CreateWebClient();

        HttpResponseMessage response = await client.PostAsync(
            new Uri("/api/diagnostics/run", UriKind.Relative), content: null);

        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task An_operator_runs_a_sweep_and_reads_a_report_covering_every_domain()
    {
        await using ZWardenWebAppFactory factory = new();
        await factory.CreateConfirmedUserAsync("op@zwarden.test", StrongPassword);
        await AuthorizationBootstrapper.EnsureSeededAsync(factory.Services, "op@zwarden.test");
        using HttpClient client = factory.CreateWebClient();
        await LoginAsync(client, "op@zwarden.test", StrongPassword);

        HttpResponseMessage run = await client.PostAsync(new Uri("/api/diagnostics/run", UriKind.Relative), content: null);

        await Assert.That(run.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using JsonDocument body = JsonDocument.Parse(await run.Content.ReadAsStringAsync());
        JsonElement checks = body.RootElement.GetProperty("checks");
        await Assert.That(checks.GetArrayLength()).IsGreaterThanOrEqualTo(12);
        // The in-process domains produce a live verdict; the base Web check always passes.
        await Assert.That(await run.Content.ReadAsStringAsync()).Contains("Web");
    }

    [Test]
    public async Task The_agent_side_domains_are_reported_skipped_in_pr_a()
    {
        await using ZWardenWebAppFactory factory = new();
        await factory.CreateConfirmedUserAsync("op@zwarden.test", StrongPassword);
        await AuthorizationBootstrapper.EnsureSeededAsync(factory.Services, "op@zwarden.test");
        using HttpClient client = factory.CreateWebClient();
        await LoginAsync(client, "op@zwarden.test", StrongPassword);

        HttpResponseMessage run = await client.PostAsync(new Uri("/api/diagnostics/run", UriKind.Relative), content: null);
        using JsonDocument body = JsonDocument.Parse(await run.Content.ReadAsStringAsync());

        JsonElement docker = body.RootElement.GetProperty("checks").EnumerateArray()
            .Single(c => c.GetProperty("domain").GetString() == "Docker");
        await Assert.That(docker.GetProperty("status").GetString()).IsEqualTo("Skipped");
    }

    [Test]
    public async Task Anonymous_cannot_trigger_a_host_gather()
    {
        await using ZWardenWebAppFactory factory = new();
        using HttpClient client = factory.CreateWebClient();

        HttpResponseMessage response = await client.PostAsync(
            new Uri($"/api/agents/{AgentId.New()}/diagnostics/gather", UriKind.Relative), content: null);

        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.Accepted);
    }

    [Test]
    public async Task An_operator_triggers_a_host_gather()
    {
        await using ZWardenWebAppFactory factory = new();
        await factory.CreateConfirmedUserAsync("op@zwarden.test", StrongPassword);
        await AuthorizationBootstrapper.EnsureSeededAsync(factory.Services, "op@zwarden.test");
        using HttpClient client = factory.CreateWebClient();
        await LoginAsync(client, "op@zwarden.test", StrongPassword);

        HttpResponseMessage gather = await client.PostAsync(
            new Uri($"/api/agents/{AgentId.New()}/diagnostics/gather", UriKind.Relative), content: null);

        await Assert.That(gather.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        using JsonDocument body = JsonDocument.Parse(await gather.Content.ReadAsStringAsync());
        await Assert.That(body.RootElement.GetProperty("operationId").GetString()).StartsWith("op-");
    }

    [Test]
    public async Task Triggering_a_server_gather_for_an_unknown_server_is_not_found()
    {
        await using ZWardenWebAppFactory factory = new();
        await factory.CreateConfirmedUserAsync("op@zwarden.test", StrongPassword);
        await AuthorizationBootstrapper.EnsureSeededAsync(factory.Services, "op@zwarden.test");
        using HttpClient client = factory.CreateWebClient();
        await LoginAsync(client, "op@zwarden.test", StrongPassword);

        HttpResponseMessage gather = await client.PostAsync(
            new Uri($"/api/servers/{ServerId.New()}/diagnostics/gather", UriKind.Relative), content: null);

        await Assert.That(gather.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
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
