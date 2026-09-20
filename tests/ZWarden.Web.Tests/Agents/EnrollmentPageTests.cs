using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Identity;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Web.Tests.Account;
using EnrollmentPage = ZWarden.Web.Components.Pages.Hosts.Enrollment;

namespace ZWarden.Web.Tests.Agents;

/// <summary>
/// The standalone, post-setup agent-enrollment surface (<c>/enrollment</c>). It replaces the first-run-only
/// <c>/setup/enroll</c> as the place operators mint enrollment tokens once setup is complete: gated server-side
/// on <c>Tenant.Enrollment.Manage</c> (Owner only — an Administrator can reach Settings but not this), rendered
/// in the normal app shell with no wizard chrome and, crucially, <b>no setup-complete redirect</b> (the bug this
/// page fixes: the only enrollment UI used to bounce to Fleet the moment setup was finished). Exercised over the
/// real host.
/// </summary>
public sealed class EnrollmentPageTests
{
    private const string StrongPassword = "correct horse battery staple";

    [Test]
    public async Task The_page_is_gated_by_the_tenant_enrollment_manage_permission_server_side()
    {
        AuthorizeAttribute attribute = typeof(EnrollmentPage)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Single();

        await Assert.That(attribute.Policy).IsEqualTo("Tenant.Enrollment.Manage");
    }

    [Test]
    public async Task Anonymous_is_redirected_from_enrollment()
    {
        await using ZWardenWebAppFactory factory = new();
        using HttpClient client = factory.CreateWebClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/enrollment", UriKind.Relative));

        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task An_administrator_cannot_reach_the_owner_only_enrollment_page()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInWithRoleAsync(factory, "admin@zwarden.test", BuiltInRoleKind.Administrator);

        HttpResponseMessage response = await client.GetAsync(new Uri("/enrollment", UriKind.Relative));

        // Tenant.Enrollment.Manage is Owner-only: an Administrator is denied at the door (access-denied / non-200).
        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.OK);
        client.Dispose();
    }

    [Test]
    public async Task An_owner_can_reach_enrollment_after_setup_is_complete()
    {
        // The regression guard: the default factory marks setup complete on start, exactly the state the old
        // /setup/enroll page redirected away from. The standalone page must render, not bounce to Fleet.
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOwnerAsync(factory, "owner@zwarden.test");

        HttpResponseMessage response = await client.GetAsync(new Uri("/enrollment", UriKind.Relative));
        string html = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(html).Contains("data-enrollment-page");
        await Assert.That(html).Contains("data-enrollment-generate"); // the token-issuing form is present
        client.Dispose();
    }

    [Test]
    public async Task An_owner_can_issue_a_one_time_token_from_the_standalone_page()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOwnerAsync(factory, "owner@zwarden.test");

        string page = await (await client.GetAsync(new Uri("/enrollment", UriKind.Relative))).Content.ReadAsStringAsync();
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "enroll-agent",
            ["Input.Label"] = "host-alpha",
        };

        HttpResponseMessage response = await client.PostAsync(
            new Uri("/enrollment", UriKind.Relative), new FormUrlEncodedContent(form));
        string html = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(html).Contains("data-enrollment-secret");
        await Assert.That(html).Contains("data-enrollment-row");
        // The one-time token carries a copy-to-clipboard control (#210) targeting the token element.
        await Assert.That(html).Contains("data-enrollment-token");
        await Assert.That(html).Contains("data-shell-copy");
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

    // Grants a built-in role tenant-wide to an existing user (mirrors SettingsPageTests).
    private static async Task GrantBuiltInRoleAsync(
        ZWardenWebAppFactory factory, string email, BuiltInRoleKind role)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        Microsoft.AspNetCore.Identity.UserManager<ApplicationUser> users =
            scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
        ApplicationUser user = await users.FindByEmailAsync(email)
            ?? throw new InvalidOperationException($"No user {email}.");
        UserId userId = UserId.FromGuid(user.Id);

        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Role builtIn = await db.Set<Role>().FirstAsync(r => r.BuiltIn == role);
        db.Add(RoleAssignment.TenantWide(Tenant.DefaultId, userId, builtIn.Id));
        await db.SaveChangesAsync();
    }

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
