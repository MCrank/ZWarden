using System.Net;
using System.Text.RegularExpressions;
using Bunit;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Settings;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Identity;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;
using ZWarden.Web.Tests.Account;
using SettingsPage = ZWarden.Web.Components.Pages.Settings.Settings;

namespace ZWarden.Web.Tests.Settings;

/// <summary>
/// #345: an Owner or Administrator renames the instance from Settings → General. The name overrides
/// <c>ZWarden:Instance:Name</c>, shows in the sidebar's workspace label and the signed-in page titles without a
/// restart, and clearing it reverts to config. Anonymous pages never show the override.
/// </summary>
public sealed class InstanceNameTests
{
    private const string StrongPassword = "correct horse battery staple";

    [Test]
    public async Task A_saved_name_shows_in_the_sidebar_and_the_page_title_but_not_before_sign_in()
    {
        await using ZWardenWebAppFactory factory = new();
        using HttpClient client = await SignedInOwnerAsync(factory, "owner@zwarden.test");
        await RenameAsync(factory, "owner@zwarden.test", "Knox <Ops>");

        string fleet = await GetStringAsync(client, "/servers");
        using HttpClient anonymous = factory.CreateWebClient();
        string login = await GetStringAsync(anonymous, "/login");

        await Assert.That(fleet).Contains("<title>Fleet · Knox &lt;Ops&gt;</title>"); // rendered as text
        await Assert.That(Regex.IsMatch(fleet, "class=\"zw-t1\">Knox &lt;Ops&gt;</span>")).IsTrue();
        await Assert.That(login).DoesNotContain("Knox");
    }

    [Test]
    public async Task Without_an_override_the_shell_shows_the_configured_name()
    {
        await using ZWardenWebAppFactory factory = new();
        using HttpClient client = await SignedInOwnerAsync(factory, "owner@zwarden.test");

        string fleet = await GetStringAsync(client, "/servers");

        await Assert.That(fleet).Contains("<title>Fleet · ZWarden</title>");
    }

    [Test]
    public async Task General_offers_the_name_editor_with_the_saved_name_and_the_config_default()
    {
        await using ZWardenWebAppFactory factory = new();
        using HttpClient client = await SignedInOwnerAsync(factory, "owner@zwarden.test");
        await RenameAsync(factory, "owner@zwarden.test", "Knox Ops");

        string html = await GetStringAsync(client, "/settings");

        await Assert.That(html).Contains("data-settings-instance-form");
        await Assert.That(Regex.IsMatch(html, "id=\"instance-name\"[^>]*value=\"Knox Ops\"|value=\"Knox Ops\"[^>]*id=\"instance-name\"")).IsTrue();
        await Assert.That(html).Contains("data-action=\"instance-name-reset\"");
        await Assert.That(html).Contains("overrides the config name &quot;ZWarden&quot;");
    }

    [Test]
    public async Task An_owner_renames_the_instance_then_resets_it_to_the_config_name()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        IRenderedComponent<SettingsPage> cut = harness.RenderPage<SettingsPage>("/settings");

        await InteractivePageHarness.TypeAsync(cut, "instance-name", "  Knox Ops ");
        await cut.Find("[data-settings-instance-form]").SubmitAsync();
        cut.WaitForState(() => cut.FindAll("[data-settings-instance-saved]").Count == 1);

        await Assert.That(cut.Find("[data-settings-instance-saved]").TextContent).Contains("saved as \"Knox Ops\"");
        await Assert.That(cut.Find("[data-settings-instance-name]").TextContent).IsEqualTo("Knox Ops");
        await Assert.That(await OverrideAsync(harness)).IsEqualTo("Knox Ops");

        await cut.Find("[data-action=instance-name-reset]").ClickAsync(new());
        cut.WaitForState(() => cut.Find("[data-settings-instance-name]").TextContent == "ZWarden");

        await Assert.That(await OverrideAsync(harness)).IsNull();
        await Assert.That(cut.FindAll("[data-action=instance-name-reset]")).IsEmpty();
    }

    [Test]
    public async Task An_invalid_name_shows_why_and_changes_nothing()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        IRenderedComponent<SettingsPage> cut = harness.RenderPage<SettingsPage>("/settings");

        await InteractivePageHarness.TypeAsync(cut, "instance-name", new string('x', 65));
        await cut.Find("[data-settings-instance-form]").SubmitAsync();
        cut.WaitForState(() => cut.FindAll("[data-settings-instance-error]").Count == 1);

        await Assert.That(cut.Find("[data-settings-instance-error]").TextContent).Contains("at most 64 characters");
        await Assert.That(await OverrideAsync(harness)).IsNull();
    }

    private static async Task<string?> OverrideAsync(InteractivePageHarness harness)
    {
        await using AsyncServiceScope scope = harness.Factory.Services.CreateSystemScope();
        return (await scope.ServiceProvider.GetRequiredService<IControlPlaneSettingsService>().GetAsync()).InstanceName;
    }

    // Renames through the real service as the given (Owner) user, in the default tenant's scope.
    private static async Task RenameAsync(ZWardenWebAppFactory factory, string email, string name)
    {
        await using AsyncServiceScope scope = factory.Services.CreateSystemScope();
        UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        ApplicationUser user = await users.FindByEmailAsync(email) ?? throw new InvalidOperationException($"No user {email}.");
        await scope.ServiceProvider.GetRequiredService<IControlPlaneSettingsService>()
            .SetInstanceNameAsync(UserId.FromGuid(user.Id), name);
    }

    private static async Task<HttpClient> SignedInOwnerAsync(ZWardenWebAppFactory factory, string email)
    {
        await factory.CreateConfirmedUserAsync(email, StrongPassword);
        await AuthorizationBootstrapper.EnsureSeededAsync(factory.Services, email); // grants Tenant Owner
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
