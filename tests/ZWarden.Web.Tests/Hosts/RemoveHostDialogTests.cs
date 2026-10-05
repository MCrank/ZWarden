using BlazorBlueprint.Primitives;
using Bunit;
using Bunit.Rendering;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Domain.Agents;
using ZWarden.Domain.Enrollments;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;
using ZWarden.Web.Components.Hosts;

namespace ZWarden.Web.Tests.Hosts;

/// <summary>
/// #363: a Host card's "Remove host" island. The alert dialog names the Host; with Servers on it, it says so and
/// offers no Remove; otherwise Remove deletes the Agent through the real <c>IAgentTrustService</c> and refreshes the
/// page. Rendered beside a <see cref="BbPortalHost"/>, as the Hosts page renders one for all its islands.
/// </summary>
public sealed class RemoveHostDialogTests
{
    private const string AgentHash = "agenthashaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Test]
    public async Task The_button_opens_a_dialog_naming_the_host_and_what_removal_does()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        AgentId agentId = await SeedAgentAsync(harness);
        IRenderedComponent<ContainerFragment> cut = Render(harness, agentId, serverCount: 0);
        await Assert.That(cut.FindAll("[data-remove-host-dialog]")).IsEmpty();

        await OpenAsync(cut);

        await Assert.That(cut.Find("[data-remove-host-dialog]").TextContent).Contains("Remove host-alpha?");
        string effects = cut.Find("[data-remove-host-effects]").TextContent;
        await Assert.That(effects).Contains("Nothing on the machine is touched");
        await Assert.That(effects).Contains("new enrollment token");
        await Assert.That(cut.FindAll("[data-action=remove-host]")).Count().IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-remove-host-blocked]")).IsEmpty();
    }

    [Test]
    public async Task A_host_with_servers_is_blocked_with_no_remove_button()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        AgentId agentId = await SeedAgentAsync(harness);
        IRenderedComponent<ContainerFragment> cut = Render(harness, agentId, serverCount: 2);

        await OpenAsync(cut);

        await Assert.That(cut.Find("[data-remove-host-blocked]").TextContent).Contains("still has 2 servers");
        await Assert.That(cut.FindAll("[data-action=remove-host]")).IsEmpty();
        await Assert.That(cut.Find("[data-action=remove-host-cancel]").TextContent.Trim()).IsEqualTo("Close");
    }

    [Test]
    public async Task Remove_deletes_the_agent_and_refreshes_the_page()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        AgentId agentId = await SeedAgentAsync(harness);
        IRenderedComponent<ContainerFragment> cut = Render(harness, agentId, serverCount: 0);
        await OpenAsync(cut);
        NavigationManager nav = harness.Context.Services.GetRequiredService<NavigationManager>();
        string before = nav.Uri;

        await cut.Find("[data-action=remove-host]").ClickAsync(new());

        cut.WaitForState(() => cut.FindAll("[data-remove-host-dialog]").Count == 0);
        await Assert.That(await FindAgentAsync(harness, agentId)).IsNull();
        await Assert.That(nav.Uri).IsEqualTo(before); // Refresh re-renders the same page
    }

    [Test]
    public async Task A_server_deployed_meanwhile_turns_the_dialog_into_the_blocked_message()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        AgentId agentId = await SeedAgentAsync(harness);
        IRenderedComponent<ContainerFragment> cut = Render(harness, agentId, serverCount: 0);
        await OpenAsync(cut);
        await SeedServerOnAsync(harness, agentId);

        await cut.Find("[data-action=remove-host]").ClickAsync(new());

        cut.WaitForState(() => cut.FindAll("[data-remove-host-blocked]").Count == 1);
        await Assert.That(cut.Find("[data-remove-host-blocked]").TextContent).Contains("still has 1 server.");
        await Assert.That(await FindAgentAsync(harness, agentId)).IsNotNull();
    }

    private static IRenderedComponent<ContainerFragment> Render(InteractivePageHarness harness, AgentId agentId, int serverCount)
        => harness.Context.Render(builder =>
        {
            builder.OpenComponent<RemoveHostDialog>(0);
            builder.AddComponentParameter(1, nameof(RemoveHostDialog.AgentId), agentId);
            builder.AddComponentParameter(2, nameof(RemoveHostDialog.HostName), "host-alpha");
            builder.AddComponentParameter(3, nameof(RemoveHostDialog.ServerCount), serverCount);
            builder.CloseComponent();
            builder.OpenComponent<BbPortalHost>(4);
            builder.CloseComponent();
        });

    private static async Task OpenAsync(IRenderedComponent<ContainerFragment> cut)
    {
        await cut.Find("[data-action=remove-host-open]").ClickAsync(new());
        cut.WaitForState(() => cut.FindAll("[data-remove-host-dialog]").Count == 1);
    }

    private static async Task<AgentId> SeedAgentAsync(InteractivePageHarness harness)
    {
        await using AsyncServiceScope scope = harness.Factory.Services.CreateSystemScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Agent agent = Agent.Enroll(AgentHash, EnrollmentId.New(), DateTimeOffset.UtcNow, "host-alpha");
        db.Add(agent);
        await db.SaveChangesAsync();
        return agent.Id;
    }

    private static async Task SeedServerOnAsync(InteractivePageHarness harness, AgentId agentId)
    {
        await using AsyncServiceScope scope = harness.Factory.Services.CreateSystemScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        db.Add(Server.Register(agentId, "late-arrival", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
    }

    private static async Task<Agent?> FindAgentAsync(InteractivePageHarness harness, AgentId agentId)
    {
        await using AsyncServiceScope scope = harness.Factory.Services.CreateSystemScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        return await db.Set<Agent>().FirstOrDefaultAsync(a => a.Id == agentId);
    }
}
