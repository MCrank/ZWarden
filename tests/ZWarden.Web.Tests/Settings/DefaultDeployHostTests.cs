using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Settings;
using ZWarden.Domain.Agents;
using ZWarden.Domain.Enrollments;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;
using SettingsPage = ZWarden.Web.Components.Pages.Settings.Settings;

namespace ZWarden.Web.Tests.Settings;

/// <summary>
/// #347: Settings → General picks the host Deploy server pre-selects, from the enrolled and enabled hosts by name. A
/// saved host that is later revoked or disabled means no default, and the page says so.
/// </summary>
public sealed class DefaultDeployHostTests
{
    [Test]
    public async Task The_picker_lists_enrolled_hosts_by_name_and_saves_the_choice()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        AgentId alpha = await SeedHostAsync(harness, "host-alpha");
        await SeedHostAsync(harness, "host-bravo", disabled: true); // not a candidate
        IRenderedComponent<SettingsPage> cut = harness.RenderPage<SettingsPage>("/settings");

        List<string> options = [.. cut.FindAll("#default-deploy-host option").Select(o => o.TextContent.Trim())];
        await Assert.That(options).IsEquivalentTo(["No default", "host-alpha"]);

        await cut.Find("#default-deploy-host").ChangeAsync(new ChangeEventArgs { Value = alpha.ToString() });
        await cut.Find("[data-settings-default-host-form]").SubmitAsync();
        cut.WaitForState(() => cut.FindAll("[data-settings-default-host-saved]").Count == 1);

        await Assert.That(cut.Find("[data-settings-default-host-saved]").TextContent).Contains("pre-selects host-alpha");
        await Assert.That(await DefaultAsync(harness)).IsEqualTo(alpha);
    }

    [Test]
    public async Task A_revoked_default_host_means_no_default_and_the_page_says_so()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        AgentId alpha = await SeedHostAsync(harness, "host-alpha");
        await SeedHostAsync(harness, "host-bravo");
        IRenderedComponent<SettingsPage> first = harness.RenderPage<SettingsPage>("/settings");
        await first.Find("#default-deploy-host").ChangeAsync(new ChangeEventArgs { Value = alpha.ToString() });
        await first.Find("[data-settings-default-host-form]").SubmitAsync();
        first.WaitForState(() => first.FindAll("[data-settings-default-host-saved]").Count == 1);

        await DisableAsync(harness, alpha);
        IRenderedComponent<SettingsPage> cut = harness.RenderPage<SettingsPage>("/settings");

        await Assert.That(cut.FindAll("[data-settings-default-host-gone]").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("#default-deploy-host option").Select(o => o.TextContent.Trim()).ToList())
            .IsEquivalentTo(["No default", "host-bravo"]);
        await Assert.That(await DefaultAsync(harness)).IsEqualTo(alpha); // kept: re-enabling the host restores it
    }

    private static async Task<AgentId?> DefaultAsync(InteractivePageHarness harness)
    {
        await using AsyncServiceScope scope = harness.Factory.Services.CreateSystemScope();
        return (await scope.ServiceProvider.GetRequiredService<IControlPlaneSettingsService>().GetAsync()).DefaultDeployHost;
    }

    private static async Task<AgentId> SeedHostAsync(InteractivePageHarness harness, string label, bool disabled = false)
    {
        await using AsyncServiceScope scope = harness.Factory.Services.CreateSystemScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Agent agent = Agent.Enroll("hash-" + label, EnrollmentId.New(), DateTimeOffset.UtcNow, label);
        if (disabled)
        {
            agent.Disable();
        }

        db.Set<Agent>().Add(agent);
        await db.SaveChangesAsync();
        return agent.Id;
    }

    private static async Task DisableAsync(InteractivePageHarness harness, AgentId id)
    {
        await using AsyncServiceScope scope = harness.Factory.Services.CreateSystemScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Agent agent = db.Set<Agent>().Single(a => a.Id == id);
        agent.Disable();
        await db.SaveChangesAsync();
    }
}
