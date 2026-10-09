using BlazorBlueprint.Primitives;
using Bunit;
using Bunit.Rendering;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Agents;
using ZWarden.Domain.Agents;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;
using ZWarden.Web.Components.Hosts;

namespace ZWarden.Web.Tests.Hosts;

/// <summary>
/// #368: the "Replace host" island on the new Host's card. The dialog says what moves and that the old Host is
/// removed; Replace runs the real <c>IHostReplacementService</c> and refreshes the page; a refusal (the new Host no
/// longer reports the old one's containers) is shown in the dialog and changes nothing.
/// </summary>
public sealed class ReplaceHostDialogTests
{
    private const string OldHash = "oldhashaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string NewHash = "newhashaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Test]
    public async Task The_button_opens_a_dialog_saying_what_moves_and_that_the_old_host_goes()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        (AgentId old, AgentId fresh) = await SeedHostsAsync(harness);
        IRenderedComponent<ContainerFragment> cut = Render(harness, fresh, old, serverCount: 2);

        await OpenAsync(cut);

        await Assert.That(cut.Find("[data-replace-host-dialog]").TextContent).Contains("Replace nsfw-2-old with nsfw-2?");
        string effects = cut.Find("[data-replace-host-effects]").TextContent;
        await Assert.That(effects).Contains("2 servers and their backups move to nsfw-2");
        await Assert.That(effects).Contains("nsfw-2-old is removed");
        await Assert.That(effects).Contains("can't be undone");
    }

    [Test]
    public async Task Replace_moves_the_servers_removes_the_old_host_and_refreshes()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        (AgentId old, AgentId fresh) = await SeedHostsAsync(harness);
        ServerId server = await SeedServerOnAsync(harness, old);
        harness.Factory.Services.GetRequiredService<IForeignContainerCache>()
            .Record(fresh, [new ReportedForeignContainer("0123456789ab", server, old, "running")]);
        IRenderedComponent<ContainerFragment> cut = Render(harness, fresh, old, serverCount: 1);
        await OpenAsync(cut);
        NavigationManager nav = harness.Context.Services.GetRequiredService<NavigationManager>();
        string before = nav.Uri;

        await cut.Find("[data-action=replace-host]").ClickAsync(new());

        cut.WaitForState(() => cut.FindAll("[data-replace-host-dialog]").Count == 0);
        await using AsyncServiceScope scope = harness.Factory.Services.CreateSystemScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        await Assert.That(await db.Set<Agent>().AnyAsync(a => a.Id == old)).IsFalse();
        await Assert.That((await db.Set<Server>().SingleAsync(s => s.Id == server)).AgentId).IsEqualTo(fresh);
        await Assert.That(nav.Uri).IsEqualTo(before);
    }

    [Test]
    public async Task A_refusal_is_shown_and_nothing_changes()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        (AgentId old, AgentId fresh) = await SeedHostsAsync(harness);
        IRenderedComponent<ContainerFragment> cut = Render(harness, fresh, old, serverCount: 0);
        await OpenAsync(cut);

        await cut.Find("[data-action=replace-host]").ClickAsync(new());

        cut.WaitForState(() => cut.FindAll("[data-replace-host-message]").Count == 1);
        await Assert.That(cut.Find("[data-replace-host-message]").TextContent).Contains("no longer reports");
        await using AsyncServiceScope scope = harness.Factory.Services.CreateSystemScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        await Assert.That(await db.Set<Agent>().AnyAsync(a => a.Id == old)).IsTrue();
    }

    private static IRenderedComponent<ContainerFragment> Render(
        InteractivePageHarness harness, AgentId successor, AgentId predecessor, int serverCount)
        => harness.Context.Render(builder =>
        {
            builder.OpenComponent<ReplaceHostDialog>(0);
            builder.AddComponentParameter(1, nameof(ReplaceHostDialog.SuccessorId), successor);
            builder.AddComponentParameter(2, nameof(ReplaceHostDialog.SuccessorName), "nsfw-2");
            builder.AddComponentParameter(3, nameof(ReplaceHostDialog.PredecessorId), predecessor);
            builder.AddComponentParameter(4, nameof(ReplaceHostDialog.PredecessorName), "nsfw-2-old");
            builder.AddComponentParameter(5, nameof(ReplaceHostDialog.ServerCount), serverCount);
            builder.CloseComponent();
            builder.OpenComponent<BbPortalHost>(6);
            builder.CloseComponent();
        });

    private static async Task OpenAsync(IRenderedComponent<ContainerFragment> cut)
    {
        await cut.Find("[data-action=replace-host-open]").ClickAsync(new());
        cut.WaitForState(() => cut.FindAll("[data-replace-host-dialog]").Count == 1);
    }

    private static async Task<(AgentId Old, AgentId Fresh)> SeedHostsAsync(InteractivePageHarness harness)
    {
        await using AsyncServiceScope scope = harness.Factory.Services.CreateSystemScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Agent old = Agent.Enroll(OldHash, EnrollmentId.New(), DateTimeOffset.UtcNow, "nsfw-2-old");
        Agent fresh = Agent.Enroll(NewHash, EnrollmentId.New(), DateTimeOffset.UtcNow, "nsfw-2");
        db.AddRange(old, fresh);
        await db.SaveChangesAsync();
        return (old.Id, fresh.Id);
    }

    private static async Task<ServerId> SeedServerOnAsync(InteractivePageHarness harness, AgentId agentId)
    {
        await using AsyncServiceScope scope = harness.Factory.Services.CreateSystemScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Server server = Server.Register(agentId, "alpha", DateTimeOffset.UtcNow);
        db.Add(server);
        await db.SaveChangesAsync();
        return server.Id;
    }
}
