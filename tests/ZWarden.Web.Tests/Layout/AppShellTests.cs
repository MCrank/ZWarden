using System.Net;
using System.Text.RegularExpressions;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Web.Tests.Account;

namespace ZWarden.Web.Tests.Layout;

/// <summary>
/// The sidebar-09 app shell rendered by <c>MainLayout</c> (#157; ADR 0040). The shell is static SSR, so
/// it is exercised over real HTTP: nav links are visibility-only (`AuthorizeView`) while enforcement stays
/// server-side (PRD 12), and Log out is a static antiforgery-protected POST to the existing /logout endpoint.
/// </summary>
public sealed class AppShellTests
{
    private const string StrongPassword = "correct horse battery staple";

    [Test]
    public async Task An_owner_sees_the_full_shell_with_all_nav_and_the_logout_control()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOwnerAsync(factory, "owner@zwarden.test");

        string html = await GetStringAsync(client, "/servers");

        await Assert.That(html).Contains("data-nav=\"fleet\"");
        await Assert.That(html).Contains("data-nav=\"hosts\"");
        await Assert.That(html).Contains("data-nav=\"settings\"");
        await Assert.That(html).Contains("data-nav=\"audit\"");
        await Assert.That(html).Contains("owner@zwarden.test");   // account menu identity
        await Assert.That(html).Contains("Self-hosted");          // env pill / workspace
        await Assert.That(html).Contains("action=\"/logout\"");   // the missing Log out control
        client.Dispose();
    }

    [Test]
    public async Task A_role_less_operator_sees_fleet_but_not_the_gated_hosts_and_audit_links()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInRoleLessAsync(factory, "plain@zwarden.test");

        string html = await GetStringAsync(client, "/servers");

        // Fleet is always shown (inventory self-filters, ADR 0018).
        await Assert.That(html).Contains("data-nav=\"fleet\"");
        // Hosts (Agent.View), Settings (User.Manage, #160) and Audit (Audit.View) gate cleanly and must be
        // hidden without the policy — a role-less operator holds none of them.
        await Assert.That(html).DoesNotContain("data-nav=\"hosts\"");
        await Assert.That(html).DoesNotContain("data-nav=\"settings\"");
        await Assert.That(html).DoesNotContain("data-nav=\"audit\"");
        client.Dispose();
    }

    [Test]
    public async Task Log_out_ends_the_session_and_returns_to_the_login_screen()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOwnerAsync(factory, "owner@zwarden.test");

        // The antiforgery token travels in the shell's logout form on any authenticated page.
        string html = await GetStringAsync(client, "/servers");
        Dictionary<string, string> form = ParseHiddenInputs(html);
        await Assert.That(form.ContainsKey("__RequestVerificationToken")).IsTrue();
        form["returnUrl"] = "/login"; // ToLocalOrHome only honours a leading-slash local path (ADR 0006)

        HttpResponseMessage logout = await client.PostAsync(
            new Uri("/logout", UriKind.Relative), new FormUrlEncodedContent(form));

        await Assert.That(logout.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(logout.Headers.Location!.ToString()).Contains("login");
        client.Dispose();
    }

    private static async Task<HttpClient> SignedInOwnerAsync(ZWardenWebAppFactory factory, string email)
    {
        await factory.CreateConfirmedUserAsync(email, StrongPassword);
        await AuthorizationBootstrapper.EnsureSeededAsync(factory.Services, email); // grants Tenant Owner
        HttpClient client = factory.CreateWebClient();
        await LoginAsync(client, email, StrongPassword);
        return client;
    }

    private static async Task<HttpClient> SignedInRoleLessAsync(ZWardenWebAppFactory factory, string email)
    {
        await factory.CreateConfirmedUserAsync(email, StrongPassword);
        await AuthorizationBootstrapper.EnsureSeededAsync(factory.Services); // roles seeded, none granted to user
        HttpClient client = factory.CreateWebClient();
        await LoginAsync(client, email, StrongPassword);
        return client;
    }

    private static async Task<string> GetStringAsync(HttpClient client, string path) =>
        await (await client.GetAsync(new Uri(path, UriKind.Relative))).Content.ReadAsStringAsync();

    private static async Task LoginAsync(HttpClient client, string email, string password)
    {
        HttpResponseMessage page = await client.GetAsync(new Uri("/login", UriKind.Relative));
        Dictionary<string, string> form = ParseHiddenInputs(await page.Content.ReadAsStringAsync());
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
