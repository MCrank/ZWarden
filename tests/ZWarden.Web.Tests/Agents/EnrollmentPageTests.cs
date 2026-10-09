using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Agents;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Identity;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Web.Tests.Account;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Web.Tests.Agents;

/// <summary>
/// The post-setup Enroll host surface. #342 moved it from the standalone <c>/enrollment</c> page into a sheet on
/// <c>/hosts</c> (see <c>EnrollHostSheetTests</c>): the page renders the sheet's island only for a caller holding
/// <c>Tenant.Enrollment.Manage</c> (Owner only — an Administrator views Hosts but gets no button), and the old URL
/// redirects to the sheet's deep link. Exercised over the real host.
/// </summary>
public sealed class EnrollmentPageTests
{
    private const string StrongPassword = "correct horse battery staple";

    [Test]
    public async Task The_old_enrollment_url_redirects_to_the_hosts_sheet()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOwnerAsync(factory, "owner@zwarden.test");

        HttpResponseMessage response = await client.GetAsync(new Uri("/enrollment", UriKind.Relative));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(response.Headers.Location!.OriginalString).IsEqualTo("/hosts?enroll=1");
        client.Dispose();
    }

    [Test]
    public async Task An_owner_gets_the_enroll_host_island_on_hosts()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOwnerAsync(factory, "owner@zwarden.test");

        HttpResponseMessage response = await client.GetAsync(new Uri("/hosts", UriKind.Relative));
        string html = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(html).Contains("data-enroll-host");
        await Assert.That(Regex.IsMatch(html, "data-action=\"enroll-host-open\"[^>]*>\\s*Enroll host\\s*<")).IsTrue();
        // The prerender doesn't open the sheet or mint anything.
        await Assert.That(html).DoesNotContain("data-enroll-host-sheet");
        client.Dispose();
    }

    [Test]
    public async Task An_administrator_sees_hosts_without_the_owner_only_enroll_button()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInWithRoleAsync(factory, "admin@zwarden.test", BuiltInRoleKind.Administrator);

        HttpResponseMessage response = await client.GetAsync(new Uri("/hosts", UriKind.Relative));
        string html = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(html).DoesNotContain("data-enroll-host");
        await Assert.That(html).DoesNotContain("enroll-host-open");
        client.Dispose();
    }

    [Test]
    public async Task An_owner_gets_a_remove_host_action_on_each_card_and_one_portal_host()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOwnerAsync(factory, "owner@zwarden.test");
        await SeedHostAsync(factory);
        await SeedHostAsync(factory);

        string html = await (await client.GetAsync(new Uri("/hosts", UriKind.Relative))).Content.ReadAsStringAsync();

        // #363: one Remove island per card; the dialog itself only opens interactively.
        await Assert.That(Regex.Count(html, "data-action=\"remove-host-open\"")).IsEqualTo(2);
        await Assert.That(html).DoesNotContain("data-remove-host-dialog");
        client.Dispose();
    }

    [Test]
    public async Task An_administrator_gets_no_remove_host_action()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInWithRoleAsync(factory, "admin@zwarden.test", BuiltInRoleKind.Administrator);
        await SeedHostAsync(factory);

        string html = await (await client.GetAsync(new Uri("/hosts", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-host-card");
        await Assert.That(html).DoesNotContain("remove-host-open");
        await Assert.That(html).DoesNotContain("data-host-remove");
        client.Dispose();
    }

    // #368: the new Host's card shows containers its Agent found stamped with another id.
    [Test]
    public async Task An_owner_can_replace_an_offline_host_whose_containers_the_new_host_reports()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOwnerAsync(factory, "owner@zwarden.test");
        AgentId old = await SeedNamedHostAsync(factory, "nsfw-2-old");
        AgentId fresh = await SeedNamedHostAsync(factory, "nsfw-2");
        ServerId orphan = ServerId.New();
        factory.Services.GetRequiredService<IForeignContainerCache>().Record(fresh,
        [
            new ReportedForeignContainer("0123456789ab", ServerId.New(), old, "running"),
            new ReportedForeignContainer("fedcba987654", orphan, AgentId.New(), "exited"),
        ]);

        string html = await (await client.GetAsync(new Uri("/hosts", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains($"data-host-replace-candidate=\"{old}\"");
        await Assert.That(html).Contains("1 container from nsfw-2-old");
        await Assert.That(Regex.Count(html, "data-action=\"replace-host-open\"")).IsEqualTo(1);
        await Assert.That(html).Contains("data-host-unmanaged");
        await Assert.That(html).Contains("docker rm -f fedcba987654");
        await Assert.That(html).Contains(orphan.ToString());
        client.Dispose();
    }

    [Test]
    public async Task A_connected_old_host_is_not_offered_for_replacement()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOwnerAsync(factory, "owner@zwarden.test");
        AgentId old = await SeedNamedHostAsync(factory, "still-here");
        AgentId fresh = await SeedNamedHostAsync(factory, "nsfw-2");
        factory.Services.GetRequiredService<IAgentConnectionRegistry>().Register(old, "conn-old", () => { });
        factory.Services.GetRequiredService<IForeignContainerCache>()
            .Record(fresh, [new ReportedForeignContainer("0123456789ab", ServerId.New(), old, "running")]);

        string html = await (await client.GetAsync(new Uri("/hosts", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).DoesNotContain("replace-host-open");
        await Assert.That(html).Contains("docker rm -f 0123456789ab");
        client.Dispose();
    }

    [Test]
    public async Task An_administrator_sees_the_reported_containers_but_cannot_replace()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInWithRoleAsync(factory, "admin@zwarden.test", BuiltInRoleKind.Administrator);
        AgentId old = await SeedNamedHostAsync(factory, "nsfw-2-old");
        AgentId fresh = await SeedNamedHostAsync(factory, "nsfw-2");
        factory.Services.GetRequiredService<IForeignContainerCache>()
            .Record(fresh, [new ReportedForeignContainer("0123456789ab", ServerId.New(), old, "running")]);

        string html = await (await client.GetAsync(new Uri("/hosts", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-host-replace-candidate");
        await Assert.That(html).DoesNotContain("replace-host-open");
        client.Dispose();
    }

    private static async Task<AgentId> SeedNamedHostAsync(ZWardenWebAppFactory factory, string label)
    {
        using AsyncServiceScope scope = factory.Services.CreateSystemScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Domain.Agents.Agent agent = Domain.Agents.Agent.Enroll(
            $"{label}hash".PadRight(64, 'a'), EnrollmentId.New(), DateTimeOffset.UtcNow, label);
        db.Add(agent);
        await db.SaveChangesAsync();
        return agent.Id;
    }

    private static async Task SeedHostAsync(ZWardenWebAppFactory factory)
    {
        using AsyncServiceScope scope = factory.Services.CreateSystemScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        db.Add(Domain.Agents.Agent.Enroll(
            "agenthashaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", EnrollmentId.New(), DateTimeOffset.UtcNow, "host-x"));
        await db.SaveChangesAsync();
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
        using AsyncServiceScope scope = factory.Services.CreateSystemScope();
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
