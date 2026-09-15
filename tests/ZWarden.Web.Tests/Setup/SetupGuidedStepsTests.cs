using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Setup;
using ZWarden.Domain.Setup;
using ZWarden.Web.Tests.Account;

namespace ZWarden.Web.Tests.Setup;

/// <summary>
/// F33 PR-B: the guided-but-skippable setup steps (enroll an Agent, add servers, run a health check) and the
/// optional "continue" path off the TLS step. Only administrator + TLS mode are mandatory, so setup can be
/// finished from any guided step, and each is reachable on an un-set-up install and redirects away once
/// complete. Exercised over the real host (ADR 0036).
/// </summary>
public sealed class SetupGuidedStepsTests
{
    private const string StrongPassword = "correct horse battery staple";

    [Test]
    public async Task The_tls_continue_action_advances_to_the_guided_steps_without_completing()
    {
        await using ZWardenWebAppFactory factory = new() { CompleteSetupOnStart = false };
        HttpClient client = await SignedInAdminAsync(factory);

        string tlsPage = await (await client.GetAsync(new Uri("/setup/tls", UriKind.Relative))).Content.ReadAsStringAsync();
        Dictionary<string, string> form = ParseHiddenInputs(tlsPage);
        form["Input.Mode"] = nameof(TlsMode.Private);
        form["Input.Action"] = "continue";

        HttpResponseMessage response = await client.PostAsync(
            new Uri("/setup/tls", UriKind.Relative), new FormUrlEncodedContent(form));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(RedirectPath(response)).IsEqualTo("/setup/enroll");

        // The mode was recorded, but setup is deliberately not complete yet.
        using IServiceScope scope = factory.Services.CreateScope();
        ISetupState setup = scope.ServiceProvider.GetRequiredService<ISetupState>();
        await Assert.That(await setup.GetTlsModeAsync()).IsEqualTo(TlsMode.Private);
        await Assert.That(await setup.IsSetupCompleteAsync()).IsFalse();

        client.Dispose();
    }

    [Test]
    public async Task The_enrollment_step_issues_a_one_time_token()
    {
        await using ZWardenWebAppFactory factory = new() { CompleteSetupOnStart = false };
        HttpClient client = await SignedInAdminAsync(factory);

        string page = await (await client.GetAsync(new Uri("/setup/enroll", UriKind.Relative))).Content.ReadAsStringAsync();
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "setup-enroll",
            ["Input.Label"] = "host-alpha",
        };

        HttpResponseMessage response = await client.PostAsync(
            new Uri("/setup/enroll", UriKind.Relative), new FormUrlEncodedContent(form));
        string html = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(html).Contains("data-enrollment-secret");
        await Assert.That(html).Contains("data-enrollment-row");
        client.Dispose();
    }

    [Test]
    public async Task A_guided_step_can_finish_setup()
    {
        await using ZWardenWebAppFactory factory = new() { CompleteSetupOnStart = false };
        HttpClient client = await SignedInAdminAsync(factory);

        string page = await (await client.GetAsync(new Uri("/setup/enroll", UriKind.Relative))).Content.ReadAsStringAsync();
        Dictionary<string, string> finish = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "setup-finish",
        };

        HttpResponseMessage response = await client.PostAsync(
            new Uri("/setup/enroll", UriKind.Relative), new FormUrlEncodedContent(finish));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(RedirectPath(response)).IsEqualTo("/");

        using IServiceScope scope = factory.Services.CreateScope();
        ISetupState setup = scope.ServiceProvider.GetRequiredService<ISetupState>();
        await Assert.That(await setup.IsSetupCompleteAsync()).IsTrue();
        client.Dispose();
    }

    [Test]
    public async Task The_servers_step_is_reachable_and_shows_the_empty_state()
    {
        await using ZWardenWebAppFactory factory = new() { CompleteSetupOnStart = false };
        HttpClient client = await SignedInAdminAsync(factory);

        string html = await (await client.GetAsync(new Uri("/setup/servers", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("Add a server");
        await Assert.That(html).Contains("data-no-hosts");
        client.Dispose();
    }

    [Test]
    public async Task The_health_step_is_reachable_and_shows_the_empty_state()
    {
        await using ZWardenWebAppFactory factory = new() { CompleteSetupOnStart = false };
        HttpClient client = await SignedInAdminAsync(factory);

        string html = await (await client.GetAsync(new Uri("/setup/health", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("Initial health check");
        await Assert.That(html).Contains("data-no-servers");
        client.Dispose();
    }

    [Test]
    public async Task A_completed_setup_redirects_the_guided_steps_away()
    {
        await using ZWardenWebAppFactory factory = new(); // setup complete on start
        await factory.CreateConfirmedUserAsync("op@zwarden.test", StrongPassword);
        await ZWarden.Infrastructure.Authorization.AuthorizationBootstrapper.EnsureSeededAsync(factory.Services, "op@zwarden.test");
        HttpClient client = factory.CreateWebClient();
        await LoginAsync(client, "op@zwarden.test", StrongPassword);

        foreach (string path in new[] { "/setup/enroll", "/setup/servers", "/setup/health" })
        {
            HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
            await Assert.That(RedirectPath(response)).IsEqualTo("/");
        }

        client.Dispose();
    }

    // Runs the first-run admin step so the returned client is a signed-in Tenant Owner on an un-set-up install.
    private static async Task<HttpClient> SignedInAdminAsync(ZWardenWebAppFactory factory)
    {
        HttpClient client = factory.CreateWebClient();
        string page = await (await client.GetAsync(new Uri("/setup", UriKind.Relative))).Content.ReadAsStringAsync();
        Dictionary<string, string> form = ParseHiddenInputs(page);
        form["Input.Email"] = "admin@zwarden.test";
        form["Input.Password"] = StrongPassword;
        form["Input.ConfirmPassword"] = StrongPassword;
        await client.PostAsync(new Uri("/setup", UriKind.Relative), new FormUrlEncodedContent(form));
        return client;
    }

    private static async Task LoginAsync(HttpClient client, string email, string password)
    {
        string html = await (await client.GetAsync(new Uri("/login", UriKind.Relative))).Content.ReadAsStringAsync();
        Dictionary<string, string> form = ParseHiddenInputs(html);
        form["Input.Email"] = email;
        form["Input.Password"] = password;
        await client.PostAsync(new Uri("/login", UriKind.Relative), new FormUrlEncodedContent(form));
    }

    private static string RedirectPath(HttpResponseMessage response)
    {
        Uri location = response.Headers.Location!;
        return location.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString;
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
