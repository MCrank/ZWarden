using System.Net;
using System.Text.RegularExpressions;
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
    public async Task An_owner_sees_all_sections_and_the_real_configured_values()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOwnerAsync(factory, "owner@zwarden.test");

        string html = await GetStringAsync(client, "/settings");

        await Assert.That(html).Contains("data-settings-page");
        await Assert.That(html).Contains("data-settings-general");
        await Assert.That(html).Contains("data-settings-security");
        await Assert.That(html).Contains("data-settings-backups");
        // Owner-gated sections.
        await Assert.That(html).Contains("data-settings-enrollment");
        await Assert.That(html).Contains("data-settings-users");
        // Real, deploy-time configured values wired into General/Security.
        await Assert.That(html).Contains("data-settings-instance-name");
        await Assert.That(html).Contains("ZWarden");            // default instance name
        await Assert.That(html).Contains("data-settings-session");
        await Assert.That(html).Contains("8 hours (sliding)");  // SessionLifetime source of truth
        await Assert.That(html).Contains("data-settings-tls-mode");
        client.Dispose();
    }

    [Test]
    public async Task The_enrollment_section_links_to_the_existing_enrollment_surface()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOwnerAsync(factory, "owner@zwarden.test");

        string html = await GetStringAsync(client, "/settings");

        await Assert.That(html).Contains("data-settings-enrollment-link");
        await Assert.That(html).Contains("/setup/enroll");
        client.Dispose();
    }

    [Test]
    public async Task An_administrator_sees_the_page_but_not_the_owner_only_enrollment_section()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInWithRoleAsync(factory, "admin@zwarden.test", BuiltInRoleKind.Administrator);

        string html = await GetStringAsync(client, "/settings");

        await Assert.That(html).Contains("data-settings-page");
        await Assert.That(html).Contains("data-settings-users");        // Role.Manage — held by Administrator
        await Assert.That(html).DoesNotContain("data-settings-enrollment"); // Tenant.Enrollment.Manage — Owner only
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
        using IServiceScope scope = factory.Services.CreateScope();
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
        using IServiceScope scope = factory.Services.CreateScope();
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
