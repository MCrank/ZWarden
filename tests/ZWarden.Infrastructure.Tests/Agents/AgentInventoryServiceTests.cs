using ZWarden.Application.Agents;
using ZWarden.Application.Authorization;
using ZWarden.Domain.Agents;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Agents;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Infrastructure.Tests.Agents;

/// <summary>
/// F35 D-1/D-2: the read-only Host inventory the operator <c>/hosts</c> page reads, gated on the tenant-wide
/// <c>Agent.View</c> permission. It projects each trusted Agent's self-reported Host facts and observed
/// connection state, overlaying the in-memory registry's authoritative "connected right now" over the
/// persisted last-known state.
/// </summary>
public class AgentInventoryServiceTests
{
    private static readonly string[] View = [Permissions.AgentView.Name];

    private static AgentInventoryService Inventory(ZWardenDbContext ctx, string[] held, IAgentConnectionRegistry connections)
        => new(new AgentRepository(ctx), new StubPermissionChecker(held), connections);

    [Test]
    public async Task ListHosts_projects_host_facts_and_observed_state()
    {
        await TrustTestHarness.WithSqlite(async options =>
        {
            AgentId connectedId;
            await using (ZWardenDbContext ctx = TrustTestHarness.Context(options))
            {
                Agent connected = Agent.Enroll(
                    TrustTestHarness.Hasher.Hash(TrustTestHarness.Hasher.Generate("zwa")),
                    EnrollmentId.New(),
                    TrustTestHarness.Now,
                    "alpha");
                connected.MarkConnected(protocolVersion: 1, TrustTestHarness.Now);
                connected.RecordHostDescriptor("pz-host-2", "1.2.3", "Linux");
                ctx.Add(connected);
                await ctx.SaveChangesAsync();
                connectedId = connected.Id;
            }

            await using (ZWardenDbContext ctx = TrustTestHarness.Context(options))
            {
                StubConnectionRegistry live = new(connectedId);
                IReadOnlyList<HostSummary> hosts = await Inventory(ctx, View, live)
                    .ListHostsAsync(TrustTestHarness.Manager);

                HostSummary host = hosts.Single();
                await Assert.That(host.Id).IsEqualTo(connectedId);
                await Assert.That(host.Label).IsEqualTo("alpha");
                await Assert.That(host.Hostname).IsEqualTo("pz-host-2");
                await Assert.That(host.AgentVersion).IsEqualTo("1.2.3");
                await Assert.That(host.OsPlatform).IsEqualTo("Linux");
                await Assert.That(host.LastProtocolVersion).IsEqualTo(1);
                await Assert.That(host.IsConnected).IsTrue();
            }
        });
    }

    [Test]
    public async Task ListHosts_reports_a_persisted_connected_agent_the_registry_has_lost_as_not_connected_now()
    {
        await TrustTestHarness.WithSqlite(async options =>
        {
            await using (ZWardenDbContext ctx = TrustTestHarness.Context(options))
            {
                Agent agent = Agent.Enroll(
                    TrustTestHarness.Hasher.Hash(TrustTestHarness.Hasher.Generate("zwa")),
                    EnrollmentId.New(),
                    TrustTestHarness.Now);
                agent.MarkConnected(protocolVersion: 1, TrustTestHarness.Now);
                ctx.Add(agent);
                await ctx.SaveChangesAsync();
            }

            await using (ZWardenDbContext ctx = TrustTestHarness.Context(options))
            {
                // Registry knows of no live connection (e.g. after a Web restart) — the live overlay wins.
                IReadOnlyList<HostSummary> hosts = await Inventory(ctx, View, new StubConnectionRegistry())
                    .ListHostsAsync(TrustTestHarness.Manager);

                HostSummary host = hosts.Single();
                await Assert.That(host.ConnectionState).IsEqualTo(AgentConnectionState.Connected);
                await Assert.That(host.IsConnected).IsFalse();
            }
        });
    }

    [Test]
    public async Task ListHosts_requires_Agent_View()
    {
        await TrustTestHarness.WithSqlite(async options =>
        {
            await using ZWardenDbContext ctx = TrustTestHarness.Context(options);

            await Assert.That(async () => await Inventory(ctx, [], new StubConnectionRegistry())
                    .ListHostsAsync(TrustTestHarness.Manager))
                .Throws<AuthorizationDeniedException>();
        });
    }
}

/// <summary>A registry that reports the given ids as connected right now, for the live overlay.</summary>
internal sealed class StubConnectionRegistry(params AgentId[] connected) : IAgentConnectionRegistry
{
    private readonly HashSet<AgentId> _connected = [.. connected];

    public void Register(AgentId agentId, string connectionId, Action abort) { }

    public void Remove(string connectionId) { }

    public bool IsConnected(AgentId agentId) => _connected.Contains(agentId);

    public string? GetConnectionId(AgentId agentId) => null;

    public bool TryAbort(AgentId agentId) => false;
}
