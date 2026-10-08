using System.Net;
using System.Text.RegularExpressions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Settings;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Identity;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;
using ZWarden.Web.Tests.Account;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SettingsPage = ZWarden.Web.Components.Pages.Settings.Settings;

namespace ZWarden.Web.Tests.Settings;

/// <summary>
/// #346: the Owner picks the operator session idle timeout under Settings → Security from a bounded list (8 hours by
/// default); an Administrator sees it read-only. Saving stores it and the summary follows.
/// </summary>
public sealed class SessionTimeoutTests
{
    private const string StrongPassword = "correct horse battery staple";

    [Test]
    public async Task The_owner_gets_a_choice_of_timeouts_with_the_current_one_selected()
    {
        await using ZWardenWebAppFactory factory = new();
        using HttpClient client = await SignedInAsync(factory, "owner@zwarden.test", role: null);

        string html = await GetStringAsync(client, "/settings/security");

        await Assert.That(html).Contains("data-settings-session-form");
        await Assert.That(Regex.Matches(html, "<option value=\"(\\d+)\"").Select(m => m.Groups[1].Value).ToList())
            .IsEquivalentTo(["30", "60", "480", "1440", "10080"]);
        await Assert.That(Regex.IsMatch(html, "<option value=\"480\" selected")).IsTrue();
        await Assert.That(html).Contains("Existing sessions pick up a change on their next request");
    }

    [Test]
    public async Task An_administrator_sees_the_timeout_read_only()
    {
        await using ZWardenWebAppFactory factory = new();
        using HttpClient client = await SignedInAsync(factory, "admin@zwarden.test", BuiltInRoleKind.Administrator);

        string html = await GetStringAsync(client, "/settings/security");

        await Assert.That(html).Contains("data-settings-session");
        await Assert.That(html).Contains("8 hours (sliding)");
        await Assert.That(html).DoesNotContain("data-settings-session-form"); // Tenant.Manage is Owner only
    }

    [Test]
    public async Task The_owner_sets_a_30_minute_timeout()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        IRenderedComponent<SettingsPage> cut = harness.RenderPage<SettingsPage>("/settings/security");

        await cut.Find("#session-timeout").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "30" });
        await cut.Find("[data-settings-session-form]").SubmitAsync();
        cut.WaitForState(() => cut.FindAll("[data-settings-session-saved]").Count == 1);

        await Assert.That(cut.Find("[data-settings-session]").TextContent).IsEqualTo("30 minutes (sliding)");
        await using AsyncServiceScope scope = harness.Factory.Services.CreateSystemScope();
        ControlPlaneSettingsSnapshot stored = await scope.ServiceProvider.GetRequiredService<IControlPlaneSettingsService>().GetAsync();
        await Assert.That(stored.SessionIdleTimeout).IsEqualTo(TimeSpan.FromMinutes(30));
    }

    private static async Task<HttpClient> SignedInAsync(ZWardenWebAppFactory factory, string email, BuiltInRoleKind? role)
    {
        await factory.CreateConfirmedUserAsync(email, StrongPassword);
        if (role is null)
        {
            await AuthorizationBootstrapper.EnsureSeededAsync(factory.Services, email); // grants Tenant Owner
        }
        else
        {
            await AuthorizationBootstrapper.EnsureSeededAsync(factory.Services);
            await using AsyncServiceScope scope = factory.Services.CreateSystemScope();
            ApplicationUser user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email)
                ?? throw new InvalidOperationException($"No user {email}.");
            ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
            Role builtIn = await db.Set<Role>().FirstAsync(r => r.BuiltIn == role);
            db.Add(RoleAssignment.TenantWide(Tenant.DefaultId, UserId.FromGuid(user.Id), builtIn.Id));
            await db.SaveChangesAsync();
        }

        HttpClient client = factory.CreateWebClient();
        HttpResponseMessage page = await client.GetAsync(new Uri("/login", UriKind.Relative));
        Dictionary<string, string> form = HiddenInputs(await page.Content.ReadAsStringAsync());
        form["Input.Email"] = email;
        form["Input.Password"] = StrongPassword;
        await client.PostAsync(new Uri("/login", UriKind.Relative), new FormUrlEncodedContent(form));
        return client;
    }

    private static Dictionary<string, string> HiddenInputs(string html)
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

    private static async Task<string> GetStringAsync(HttpClient client, string path) =>
        await (await client.GetAsync(new Uri(path, UriKind.Relative))).Content.ReadAsStringAsync();
}
