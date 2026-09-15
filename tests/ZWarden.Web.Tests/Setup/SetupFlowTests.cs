using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Setup;
using ZWarden.Domain.Setup;
using ZWarden.Web.Tests.Account;

namespace ZWarden.Web.Tests.Setup;

/// <summary>
/// F33: the first-run gate and the /setup wizard, exercised over the real host (ADR 0036). Until setup is
/// complete every navigation is redirected to /setup; the wizard creates the first administrator, signs them
/// in, records the TLS mode, and marks setup complete — after which the gate lifts. A configured deployment
/// (the factory's default) never sees the gate.
/// </summary>
public sealed class SetupFlowTests
{
    private const string StrongPassword = "correct horse battery staple";

    [Test]
    public async Task An_un_set_up_install_redirects_the_root_to_the_wizard()
    {
        await using ZWardenWebAppFactory factory = new() { CompleteSetupOnStart = false };
        using HttpClient client = factory.CreateWebClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/", UriKind.Relative));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(RedirectPath(response)).IsEqualTo("/setup");
    }

    [Test]
    public async Task An_un_set_up_install_routes_even_authorized_pages_to_the_wizard_not_login()
    {
        await using ZWardenWebAppFactory factory = new() { CompleteSetupOnStart = false };
        using HttpClient client = factory.CreateWebClient();

        // /servers is [Authorize]; during first-run the gate wins over the auth challenge, so it lands on
        // /setup rather than /login.
        HttpResponseMessage response = await client.GetAsync(new Uri("/servers", UriKind.Relative));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(RedirectPath(response)).IsEqualTo("/setup");
    }

    [Test]
    public async Task The_wizard_first_step_is_reachable_on_an_un_set_up_install()
    {
        await using ZWardenWebAppFactory factory = new() { CompleteSetupOnStart = false };
        using HttpClient client = factory.CreateWebClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/setup", UriKind.Relative));
        string html = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(html).Contains("Create the first administrator");
        await Assert.That(html).Contains("data-setup-steps");
    }

    [Test]
    public async Task Framework_assets_are_not_gated()
    {
        await using ZWardenWebAppFactory factory = new() { CompleteSetupOnStart = false };
        using HttpClient client = factory.CreateWebClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/_framework/blazor.web.js", UriKind.Relative));

        // Served (or otherwise handled) — never redirected to /setup.
        bool redirectedToSetup = response.StatusCode == HttpStatusCode.Redirect
            && response.Headers.Location?.ToString() == "/setup";
        await Assert.That(redirectedToSetup).IsFalse();
    }

    [Test]
    public async Task The_first_run_flow_creates_an_admin_signs_in_and_completes_setup()
    {
        await using ZWardenWebAppFactory factory = new() { CompleteSetupOnStart = false };
        using HttpClient client = factory.CreateWebClient();

        // Step 1 — create the first administrator (the POST binds the static-SSR form end to end).
        string adminPage = await (await client.GetAsync(new Uri("/setup", UriKind.Relative))).Content.ReadAsStringAsync();
        Dictionary<string, string> adminForm = ParseHiddenInputs(adminPage);
        adminForm["Input.Email"] = "admin@zwarden.test";
        adminForm["Input.Password"] = StrongPassword;
        adminForm["Input.ConfirmPassword"] = StrongPassword;

        HttpResponseMessage created = await client.PostAsync(
            new Uri("/setup", UriKind.Relative), new FormUrlEncodedContent(adminForm));
        await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(RedirectPath(created)).Contains("/setup/tls");

        // Step 2 — the now signed-in admin reaches the TLS step and completes setup.
        string tlsPage = await (await client.GetAsync(new Uri("/setup/tls", UriKind.Relative))).Content.ReadAsStringAsync();
        await Assert.That(tlsPage).Contains("Confirm your TLS mode");

        Dictionary<string, string> tlsForm = ParseHiddenInputs(tlsPage);
        tlsForm["Input.Mode"] = nameof(TlsMode.Public);
        HttpResponseMessage done = await client.PostAsync(
            new Uri("/setup/tls", UriKind.Relative), new FormUrlEncodedContent(tlsForm));
        await Assert.That(done.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(RedirectPath(done)).IsEqualTo("/");

        // The gate has lifted: the authenticated admin now reaches an authorized page.
        HttpResponseMessage servers = await client.GetAsync(new Uri("/servers", UriKind.Relative));
        await Assert.That(servers.StatusCode).IsEqualTo(HttpStatusCode.OK);

        // Completion and the declared TLS mode persisted.
        using IServiceScope scope = factory.Services.CreateScope();
        ISetupState setup = scope.ServiceProvider.GetRequiredService<ISetupState>();
        await Assert.That(await setup.IsSetupCompleteAsync()).IsTrue();
        await Assert.That(await setup.GetTlsModeAsync()).IsEqualTo(TlsMode.Public);
    }

    [Test]
    public async Task The_admin_step_is_skipped_when_an_administrator_already_exists()
    {
        await using ZWardenWebAppFactory factory = new() { CompleteSetupOnStart = false };
        await factory.CreateConfirmedUserAsync("existing@zwarden.test", StrongPassword);
        using HttpClient client = factory.CreateWebClient();

        // No anonymous creation of a second administrator: with a user present, /setup moves on to the TLS step.
        HttpResponseMessage response = await client.GetAsync(new Uri("/setup", UriKind.Relative));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(RedirectPath(response)).Contains("/setup/tls");
    }

    [Test]
    public async Task A_completed_setup_redirects_the_wizard_back_to_the_app()
    {
        // Default factory marks setup complete on start (a configured deployment).
        await using ZWardenWebAppFactory factory = new();
        using HttpClient client = factory.CreateWebClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/setup", UriKind.Relative));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(RedirectPath(response)).IsEqualTo("/");
    }

    // The gate redirects with a relative Location ("/setup"); NavigationManager (the wizard pages) redirects
    // with an absolute one ("https://localhost/setup/tls"). Compare on the path either way.
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
