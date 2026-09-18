using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Identity;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Web.Components.Pages.Audit;
using ZWarden.Web.Tests.Account;

namespace ZWarden.Web.Tests.Audit;

/// <summary>
/// F6/#161: the administrative audit viewer, re-skinned into the Signal filter-bar + dense BbDataGrid. It is gated
/// tenant-wide by the <c>Audit.View</c> permission server-side and renders the tenant-scoped trail read through
/// <see cref="ZWarden.Application.Audit.IAuditQuery"/> (PRD 12 — enforcement, not UI visibility). Denied outcomes
/// are visually distinct, and the CSV export honours the same authorization. Exercised over the real host, like the
/// other Signal pages, since the page composes UserManager + IServerInventory to resolve actor/target names.
/// </summary>
public sealed class AuditViewerTests
{
    private const string StrongPassword = "correct horse battery staple";

    [Test]
    public async Task The_page_is_gated_by_the_audit_view_permission_server_side()
    {
        AuthorizeAttribute attribute = typeof(AuditLog)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Single();

        await Assert.That(attribute.Policy).IsEqualTo("Audit.View");
    }

    [Test]
    public async Task Anonymous_request_to_audit_redirects_to_login()
    {
        await using ZWardenWebAppFactory factory = new();
        using HttpClient client = factory.CreateWebClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/audit", UriKind.Relative));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        Uri location = response.Headers.Location ?? throw new InvalidOperationException("No Location header.");
        string path = location.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString;
        await Assert.That(path).StartsWith("/login");
    }

    [Test]
    public async Task An_operator_with_audit_view_sees_the_filter_bar_and_empty_state()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOwnerAsync(factory, "owner@zwarden.test");

        // Signing in itself writes audit events, so the trail is never genuinely empty; a filter that matches
        // nothing exercises the empty state deterministically.
        string html = await GetStringAsync(client, "/audit?correlation=no-such-correlation-xyz");

        await Assert.That(html).Contains("Audit");
        await Assert.That(html).Contains("data-audit-filter");
        await Assert.That(html).Contains("No audit events match.");
        client.Dispose();
    }

    [Test]
    public async Task Seeded_events_render_with_their_action_and_result()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOwnerAsync(factory, "owner@zwarden.test");
        await SeedEventAsync(factory, "Role.Created", AuditOutcome.Succeeded);
        await SeedEventAsync(factory, "Authentication.SignInFailed", AuditOutcome.Failed);

        string html = await GetStringAsync(client, "/audit");

        await Assert.That(html).Contains("data-audit-row");
        await Assert.That(html).Contains("Role.Created");
        await Assert.That(html).Contains("Authentication.SignInFailed");
        await Assert.That(html).Contains("OK");        // Succeeded outcome badge
        await Assert.That(html).Contains("FAILED");    // Failed outcome badge
        client.Dispose();
    }

    [Test]
    public async Task A_denied_event_is_rendered_distinctly()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOwnerAsync(factory, "owner@zwarden.test");
        await SeedEventAsync(factory, "Console.Execute", AuditOutcome.Denied);

        string html = await GetStringAsync(client, "/audit");

        // The OutcomeBadge fills the denied chip with the unhealthy hue so a denial reads at a glance.
        await Assert.That(html).Contains("DENIED");
        await Assert.That(html).Contains("bg-status-unhealthy");
        client.Dispose();
    }

    [Test]
    public async Task The_actor_id_is_resolved_to_a_display_name()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOwnerAsync(factory, "owner@zwarden.test");
        UserId owner = await UserIdOfAsync(factory, "owner@zwarden.test");
        await SeedEventAsync(factory, "Role.Created", AuditOutcome.Succeeded, actor: owner);

        string html = await GetStringAsync(client, "/audit");

        // The bare usr- id is resolved to the actor's email for the operator.
        await Assert.That(html).Contains("owner@zwarden.test");
        client.Dispose();
    }

    [Test]
    public async Task The_filter_bar_carries_the_query_keys_and_the_action_options()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOwnerAsync(factory, "owner@zwarden.test");
        await SeedEventAsync(factory, "Role.Created", AuditOutcome.Succeeded);

        string html = await GetStringAsync(client, "/audit");

        await Assert.That(html).Contains("name=\"action\"");
        await Assert.That(html).Contains("name=\"outcome\"");
        await Assert.That(html).Contains("name=\"correlation\"");
        await Assert.That(html).Contains("name=\"from\"");
        await Assert.That(html).Contains("name=\"to\"");
        // The action select is populated from the distinct actions actually present in the tenant's trail.
        await Assert.That(html).Contains("<option value=\"Role.Created\"");
        client.Dispose();
    }

    [Test]
    public async Task The_page_offers_a_csv_export_carrying_the_current_filter()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOwnerAsync(factory, "owner@zwarden.test");

        string html = await GetStringAsync(client, "/audit?outcome=Denied");

        await Assert.That(html).Contains("data-audit-export");
        await Assert.That(html).Contains("/audit/export?outcome=Denied");
        client.Dispose();
    }

    [Test]
    public async Task The_export_endpoint_requires_authorization()
    {
        await using ZWardenWebAppFactory factory = new();
        using HttpClient client = factory.CreateWebClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/audit/export", UriKind.Relative));

        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task The_export_endpoint_streams_the_filtered_trail_as_csv()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOwnerAsync(factory, "owner@zwarden.test");
        await SeedEventAsync(factory, "Role.Created", AuditOutcome.Succeeded);

        HttpResponseMessage response = await client.GetAsync(new Uri("/audit/export", UriKind.Relative));
        string csv = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("text/csv");
        await Assert.That(csv).Contains("Time (UTC),Actor,Action,Target,Result,Correlation,Detail");
        await Assert.That(csv).Contains("Role.Created");
        client.Dispose();
    }

    private static async Task SeedEventAsync(
        ZWardenWebAppFactory factory,
        string action,
        AuditOutcome outcome,
        UserId? actor = null,
        ServerId? server = null)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        db.Set<AuditEvent>().Add(AuditEvent.Create(
            action, outcome, DateTimeOffset.UtcNow, actorUserId: actor, serverId: server));
        await db.SaveChangesAsync();
    }

    private static async Task<UserId> UserIdOfAsync(ZWardenWebAppFactory factory, string email)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        Microsoft.AspNetCore.Identity.UserManager<ApplicationUser> users =
            scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
        ApplicationUser user = await users.FindByEmailAsync(email)
            ?? throw new InvalidOperationException($"No user {email}.");
        return UserId.FromGuid(user.Id);
    }

    private static async Task<HttpClient> SignedInOwnerAsync(ZWardenWebAppFactory factory, string email)
    {
        await factory.CreateConfirmedUserAsync(email, StrongPassword);
        await AuthorizationBootstrapper.EnsureSeededAsync(factory.Services, email); // grants Tenant Owner (has Audit.View)
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
