using System.Net;
using System.Text.RegularExpressions;
using Bunit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Identity;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Web.Tests.Account;
using SettingsPage = ZWarden.Web.Components.Pages.Settings.Settings;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Web.Tests.Settings;

/// <summary>
/// #160: the control-plane <c>/settings</c> page. It is the tenant-administrator surface, gated server-side on
/// the <c>User.Manage</c> policy — which exactly the Tenant Owner and Administrator hold and no operational or
/// read-only role does (enforcement, not UI visibility — PRD 12). General and Security are wired to the real,
/// deploy-time configured values; the Owner-only Enrollment section is gated a second time on
/// <c>Tenant.Enrollment.Manage</c>, and Users &amp; roles on <c>Role.Manage</c>. Exercised over real HTTP.
/// </summary>
public sealed class SettingsPageTests
{
    private const string StrongPassword = "correct horse battery staple";

    [Test]
    public async Task The_page_is_gated_by_the_user_manage_permission_server_side()
    {
        AuthorizeAttribute attribute = typeof(SettingsPage)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Single();

        await Assert.That(attribute.Policy).IsEqualTo("User.Manage");
    }

    [Test]
    public async Task Anonymous_is_redirected_from_settings()
    {
        await using ZWardenWebAppFactory factory = new();
        using HttpClient client = factory.CreateWebClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/settings", UriKind.Relative));

        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task A_non_privileged_operator_cannot_reach_settings()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInRoleLessAsync(factory, "plain@zwarden.test");

        HttpResponseMessage response = await client.GetAsync(new Uri("/settings", UriKind.Relative));

        // Without User.Manage the endpoint denies access (access-denied redirect / non-200), never renders it.
        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.OK);
        client.Dispose();
    }

    [Test]
    public async Task Settings_opens_on_general_with_a_sub_nav_of_every_section_an_owner_can_open()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOwnerAsync(factory, "owner@zwarden.test");

        string html = await GetStringAsync(client, "/settings");

        await Assert.That(html).Contains("data-settings-page");
        await Assert.That(ActiveSection(html)).IsEqualTo("general");
        await Assert.That(NavItems(html)).IsEquivalentTo(
            ["general", "security", "enrollment", "integrations", "backups", "users", "about"]);
        await Assert.That(Regex.IsMatch(html, "class=\"zw-railitem active\"[^>]*data-settings-nav-item=\"general\"")).IsTrue();
        await Assert.That(html).Contains("href=\"/settings/security\"");
        // Only the active section renders.
        await Assert.That(html).Contains("data-settings-general");
        await Assert.That(html).Contains("data-settings-time"); // #211: time display, per user, under General
        await Assert.That(html).DoesNotContain("data-settings-security");
        client.Dispose();
    }

    [Test]
    [Arguments("general", "data-settings-general")]
    [Arguments("security", "data-settings-security")]
    [Arguments("enrollment", "data-settings-enrollment")]
    [Arguments("integrations", "data-settings-workshop")]
    [Arguments("backups", "data-settings-backups")]
    [Arguments("users", "data-settings-users")]
    [Arguments("about", "data-settings-about")]
    public async Task Each_section_opens_at_its_own_url(string section, string hook)
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOwnerAsync(factory, "owner@zwarden.test");

        string html = await GetStringAsync(client, $"/settings/{section}");

        await Assert.That(ActiveSection(html)).IsEqualTo(section);
        await Assert.That(html).Contains(hook);
        client.Dispose();
    }

    [Test]
    public async Task An_unknown_section_falls_back_to_general()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOwnerAsync(factory, "owner@zwarden.test");

        string html = await GetStringAsync(client, "/settings/nope");

        await Assert.That(ActiveSection(html)).IsEqualTo("general");
        client.Dispose();
    }

    [Test]
    public async Task General_and_security_show_the_real_configured_values_as_set_in_config()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOwnerAsync(factory, "owner@zwarden.test");

        string general = await GetStringAsync(client, "/settings");
        string security = await GetStringAsync(client, "/settings/security");

        await Assert.That(general).Contains("data-settings-instance-name");
        await Assert.That(general).Contains("ZWarden");            // default instance name
        await Assert.That(general).Contains("ZWarden:Instance:Name"); // the "set in config" hint
        await Assert.That(security).Contains("data-settings-session");
        await Assert.That(security).Contains("8 hours (sliding)");  // SessionLifetime source of truth
        await Assert.That(security).Contains("data-settings-tls-mode");
        await Assert.That(security).Contains("data-settings-forwarded");
        client.Dispose();
    }

    [Test]
    public async Task The_enrollment_section_links_to_the_enroll_host_sheet_and_counts_enrolled_hosts()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOwnerAsync(factory, "owner@zwarden.test");

        string html = await GetStringAsync(client, "/settings/enrollment");

        await Assert.That(html).Contains("data-settings-enrollment-link");
        await Assert.That(html).Contains("href=\"/hosts?enroll=1\""); // #342: the Enroll host sheet
        // #343: "Host enrollment" section, "Enroll host" action.
        await Assert.That(html).Contains("Host enrollment");
        await Assert.That(Regex.IsMatch(html, "data-settings-enrollment-link[^>]*>[\\s\\S]*?Enroll host\\s*</a>")).IsTrue();
        await Assert.That(html).DoesNotContain("Agent enrollment");
        await Assert.That(html).DoesNotContain("Manage enrollment");
        await Assert.That(Regex.IsMatch(html, "data-settings-hosts-count[^>]*>\\s*0 enrolled · 0 online\\s*<")).IsTrue();
        client.Dispose();
    }

    [Test]
    public async Task About_shows_the_version_the_licence_and_the_docs()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOwnerAsync(factory, "owner@zwarden.test");

        string html = await GetStringAsync(client, "/settings/about");

        await Assert.That(html).Contains("data-settings-version");
        await Assert.That(html).Contains("Business Source License 1.1");
        await Assert.That(html).Contains("href=\"https://github.com/MCrank/ZWarden\"");
        client.Dispose();
    }

    [Test]
    public async Task An_administrator_sees_neither_the_owner_only_sections_nor_their_nav_items()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInWithRoleAsync(factory, "admin@zwarden.test", BuiltInRoleKind.Administrator);

        string html = await GetStringAsync(client, "/settings");
        string enrollment = await GetStringAsync(client, "/settings/enrollment");
        string users = await GetStringAsync(client, "/settings/users");

        await Assert.That(html).Contains("data-settings-page");
        // Tenant.Enrollment.Manage and Tenant.Manage are Owner only; Role.Manage is held by Administrator.
        await Assert.That(NavItems(html)).IsEquivalentTo(["general", "security", "backups", "users", "about"]);
        await Assert.That(ActiveSection(enrollment)).IsEqualTo("general");
        await Assert.That(enrollment).DoesNotContain("data-settings-enrollment");
        await Assert.That(ActiveSection(users)).IsEqualTo("users");
        client.Dispose();
    }

    [Test]
    public async Task The_instance_name_reflects_configuration()
    {
        await using ZWardenWebAppFactory factory = new();
        using HttpClient client = await SignedInOwnerAsync(
            factory.WithWebHostBuilder(b => b.UseSetting("ZWarden:Instance:Name", "Northwind PZ")),
            "owner@zwarden.test");

        string html = await GetStringAsync(client, "/settings");

        await Assert.That(html).Contains("Northwind PZ");
    }

    [Test]
    public async Task An_owner_sees_the_workshop_search_section_keyless_by_default()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOwnerAsync(factory, "owner@zwarden.test");

        string html = await GetStringAsync(client, "/settings/integrations");

        await Assert.That(html).Contains("data-settings-workshop");
        await Assert.That(html).Contains("data-settings-workshop-status");
        await Assert.That(html).Contains("No search key configured");
        client.Dispose();
    }

    [Test]
    public async Task An_administrator_does_not_see_the_owner_only_workshop_section()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInWithRoleAsync(factory, "admin@zwarden.test", BuiltInRoleKind.Administrator);

        string html = await GetStringAsync(client, "/settings/integrations");

        // Workshop search is gated on Tenant.Manage — Owner only, not Administrator.
        await Assert.That(html).DoesNotContain("data-settings-workshop");
        await Assert.That(ActiveSection(html)).IsEqualTo("general");
        client.Dispose();
    }

    [Test]
    public async Task An_owner_can_configure_then_clear_the_search_key_without_the_key_ever_being_echoed()
    {
        // The save and clear are circuit handlers on the interactive page (#299), driven in bUnit on the real host.
        const string key = "ABCDEF0123456789ABCDEF0123456789";
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        IRenderedComponent<SettingsPage> cut = harness.RenderPage<SettingsPage>("/settings/integrations");

        // Configure the key via the write-only form.
        await InteractivePageHarness.TypeAsync(cut, "workshop-key", key);
        await cut.Find("[data-settings-workshop] form").SubmitAsync();
        cut.WaitForState(() => cut.Markup.Contains("A search key is configured", StringComparison.Ordinal));
        await Assert.That(cut.Markup).DoesNotContain(key); // write-only: never echoed back

        // A fresh page still reports configured and still never shows the key.
        IRenderedComponent<SettingsPage> reloaded = harness.RenderPage<SettingsPage>("/settings/integrations");
        await Assert.That(reloaded.Markup).Contains("A search key is configured");
        await Assert.That(reloaded.Markup).DoesNotContain(key);

        // Clear it.
        await reloaded.Find("[data-settings-workshop] button.bb\\:bg-destructive, [data-settings-workshop] button:not([type=submit])").ClickAsync(new());
        reloaded.WaitForState(() => reloaded.Markup.Contains("No search key configured", StringComparison.Ordinal));
    }

    private static async Task<HttpClient> SignedInOwnerAsync(ZWardenWebAppFactory factory, string email)
    {
        await factory.CreateConfirmedUserAsync(email, StrongPassword);
        await AuthorizationBootstrapper.EnsureSeededAsync(factory.Services, email); // grants Tenant Owner
        HttpClient client = factory.CreateWebClient();
        await LoginAsync(client, email, StrongPassword);
        return client;
    }

    // WithWebHostBuilder returns a base WebApplicationFactory; sign a user in against it directly.
    private static async Task<HttpClient> SignedInOwnerAsync(
        WebApplicationFactory<Program> factory, string email)
    {
        await CreateConfirmedUserAsync(factory, email, StrongPassword);
        await AuthorizationBootstrapper.EnsureSeededAsync(factory.Services, email);
        HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        await LoginAsync(client, email, StrongPassword);
        return client;
    }

    private static async Task<HttpClient> SignedInRoleLessAsync(ZWardenWebAppFactory factory, string email)
    {
        await factory.CreateConfirmedUserAsync(email, StrongPassword);
        await AuthorizationBootstrapper.EnsureSeededAsync(factory.Services); // roles seeded, none granted
        HttpClient client = factory.CreateWebClient();
        await LoginAsync(client, email, StrongPassword);
        return client;
    }

    private static async Task<HttpClient> SignedInWithRoleAsync(
        ZWardenWebAppFactory factory, string email, BuiltInRoleKind role)
    {
        await factory.CreateConfirmedUserAsync(email, StrongPassword);
        await AuthorizationBootstrapper.EnsureSeededAsync(factory.Services); // seed roles, grant none
        await GrantBuiltInRoleAsync(factory, email, role);
        HttpClient client = factory.CreateWebClient();
        await LoginAsync(client, email, StrongPassword);
        return client;
    }

    // Grants a built-in role tenant-wide to an existing user, mirroring BuiltInRoleSeeder's owner assignment.
    private static async Task GrantBuiltInRoleAsync(
        ZWardenWebAppFactory factory, string email, BuiltInRoleKind role)
    {
        using AsyncServiceScope scope = factory.Services.CreateSystemScope();
        Microsoft.AspNetCore.Identity.UserManager<ApplicationUser> users =
            scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
        ApplicationUser user = await users.FindByEmailAsync(email)
            ?? throw new InvalidOperationException($"No user {email}.");
        UserId userId = UserId.FromGuid(user.Id);

        // No HttpContext in this seeding scope, so the context resolves the single default tenant — the same
        // tenant AuthorizationBootstrapper seeded the roles under, and the user's own tenant (single-tenant v1.0).
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Role builtIn = await db.Set<Role>().FirstAsync(r => r.BuiltIn == role);
        db.Add(RoleAssignment.TenantWide(Tenant.DefaultId, userId, builtIn.Id));
        await db.SaveChangesAsync();
    }

    private static async Task CreateConfirmedUserAsync(
        WebApplicationFactory<Program> factory, string email, string password)
    {
        using AsyncServiceScope scope = factory.Services.CreateSystemScope();
        Microsoft.AspNetCore.Identity.UserManager<ApplicationUser> users =
            scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
        ApplicationUser user = new(email) { Email = email, EmailConfirmed = true };
        Microsoft.AspNetCore.Identity.IdentityResult result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not seed test user: {string.Join(", ", result.Errors.Select(e => e.Code))}.");
        }
    }

    private static string ActiveSection(string html) =>
        Regex.Match(html, "data-settings-section=\"([^\"]*)\"").Groups[1].Value;

    private static List<string> NavItems(string html) =>
        Regex.Matches(html, "data-settings-nav-item=\"([^\"]*)\"").Select(m => m.Groups[1].Value).ToList();

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
