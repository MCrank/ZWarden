using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Servers;
using ZWarden.Domain.Agents;
using ZWarden.Domain.Enrollments;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;
using ZWarden.Web.Components.Servers;

namespace ZWarden.Web.Tests.Servers;

/// <summary>
/// #357: the Fleet adopt banner re-reads the per-Host counts itself, so containers an Agent reports on (re)connect show
/// up while the page is open, and an adopted one clears it — no reload. Runs over the real host (the island reads through
/// ActionScopeRunner into the real inventory), with a short poll.
/// </summary>
public sealed class UnmanagedBannerTests
{
    private const string AgentHash = "agenthashaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [Test]
    public async Task The_banner_appears_once_a_host_reports_an_unmanaged_container()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        AgentId agent = await SeedHostAsync(harness, "host-alpha");
        IRenderedComponent<UnmanagedBanner> cut = Render(harness, []);
        await Assert.That(cut.FindAll("[data-unmanaged-banner]")).IsEmpty();

        harness.Factory.Services.GetRequiredService<IServerDiscoveryCache>()
            .Record(agent, [new DiscoveredServer(ServerId.New(), ServerRunState.Running)]);

        cut.WaitForState(() => cut.FindAll("[data-unmanaged-banner]").Count == 1, Wait);
        await Assert.That(cut.Find("[data-unmanaged-text]").TextContent).IsEqualTo("1 unmanaged server found on host-alpha.");
    }

    [Test]
    public async Task The_banner_clears_once_the_container_has_a_server_record()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        AgentId agent = await SeedHostAsync(harness, "host-alpha");
        ServerId orphan = ServerId.New();
        harness.Factory.Services.GetRequiredService<IServerDiscoveryCache>()
            .Record(agent, [new DiscoveredServer(orphan, ServerRunState.Running)]);
        IRenderedComponent<UnmanagedBanner> cut = Render(harness, [new UnmanagedHost("host-alpha", 1)]);
        await Assert.That(cut.FindAll("[data-unmanaged-banner]").Count).IsEqualTo(1);

        await using (AsyncServiceScope scope = harness.Factory.Services.CreateSystemScope())
        {
            ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
            db.Set<Server>().Add(Server.Import(agent, orphan, "adopted", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        cut.WaitForState(() => cut.FindAll("[data-unmanaged-banner]").Count == 0, Wait);
    }

    [Test]
    public async Task Several_hosts_are_counted_and_named_in_the_title()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(prerendering: true);

        IRenderedComponent<UnmanagedBanner> cut = Render(harness, [new UnmanagedHost("host-alpha", 2), new UnmanagedHost("host-bravo", 1)]);

        await Assert.That(cut.Find("[data-unmanaged-text]").TextContent).IsEqualTo("3 unmanaged servers found on 2 hosts.");
        await Assert.That(cut.Find("[data-unmanaged-text]").GetAttribute("title")).IsEqualTo("host-alpha, host-bravo");
    }

    private static IRenderedComponent<UnmanagedBanner> Render(InteractivePageHarness harness, IReadOnlyList<UnmanagedHost> hosts) =>
        harness.Context.Render<UnmanagedBanner>(p => p
            .Add(c => c.Hosts, hosts)
            .Add(c => c.PollInterval, Poll));

    private static async Task<AgentId> SeedHostAsync(InteractivePageHarness harness, string label)
    {
        await using AsyncServiceScope scope = harness.Factory.Services.CreateSystemScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Agent agent = Agent.Enroll(AgentHash, EnrollmentId.New(), DateTimeOffset.UtcNow, label);
        db.Set<Agent>().Add(agent);
        await db.SaveChangesAsync();
        return agent.Id;
    }
}
