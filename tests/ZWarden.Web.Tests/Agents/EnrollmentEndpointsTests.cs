using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Web.Tests.Account;

namespace ZWarden.Web.Tests.Agents;

/// <summary>
/// F9 S5 (PR 1): the operator enrollment/trust API is gated by <c>Tenant.Enrollment.Manage</c> (F5) — an
/// anonymous or under-permissioned caller cannot reach it — and a freshly minted secret is returned exactly
/// once (never in the listing). Exercised over the real host (ADR 0007).
/// </summary>
public sealed class EnrollmentEndpointsTests
{
    private const string StrongPassword = "correct horse battery staple";

    [Test]
    public async Task Anonymous_cannot_reach_the_operator_api()
    {
        await using ZWardenWebAppFactory factory = new();
        using HttpClient client = factory.CreateWebClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/api/agents", UriKind.Relative));

        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task An_authenticated_user_without_the_permission_is_denied()
    {
        await using ZWardenWebAppFactory factory = new();
        await factory.CreateConfirmedUserAsync("plain@zwarden.test", StrongPassword);
        using HttpClient client = factory.CreateWebClient();
        await LoginAsync(client, "plain@zwarden.test", StrongPassword);

        HttpResponseMessage response = await client.GetAsync(new Uri("/api/agents", UriKind.Relative));

        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task An_operator_mints_a_token_whose_secret_is_returned_once_and_never_listed()
    {
        await using ZWardenWebAppFactory factory = new();
        await factory.CreateConfirmedUserAsync("op@zwarden.test", StrongPassword);
        await AuthorizationBootstrapper.EnsureSeededAsync(factory.Services, "op@zwarden.test");
        using HttpClient client = factory.CreateWebClient();
        await LoginAsync(client, "op@zwarden.test", StrongPassword);

        HttpResponseMessage mint = await client.PostAsJsonAsync(
            new Uri("/api/enrollments", UriKind.Relative),
            new { label = "host-alpha" });
        string mintBody = await mint.Content.ReadAsStringAsync();

        await Assert.That(mint.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(mintBody).Contains("\"secret\"");
        await Assert.That(mintBody).Contains("zwe_");

        HttpResponseMessage list = await client.GetAsync(new Uri("/api/enrollments", UriKind.Relative));
        string listBody = await list.Content.ReadAsStringAsync();

        await Assert.That(list.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(listBody).Contains("host-alpha");
        await Assert.That(listBody).DoesNotContain("secret"); // the secret is shown once, never in the listing
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
