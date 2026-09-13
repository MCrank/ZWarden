using ZWarden.Application.Servers;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Servers;

namespace ZWarden.Infrastructure.Tests.Servers;

/// <summary>
/// F16 PR-C: the live health cache keeps the latest rollup per Server and enforces the same ownership guard as
/// the metrics cache — a rollup is returned only to a caller naming the Agent that reported it.
/// </summary>
public class ServerHealthCacheTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task GetLatest_returns_the_last_recorded_rollup_for_its_owning_agent()
    {
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();
        ServerHealthCache cache = new();

        cache.Record(new ServerLiveHealth(agent, server, ServerHealth.Healthy, "ok", At));
        cache.Record(new ServerLiveHealth(agent, server, ServerHealth.Degraded, "a port is unreachable", At.AddMinutes(1)));

        ServerLiveHealth? latest = cache.GetLatest(server, agent);
        await Assert.That(latest).IsNotNull();
        await Assert.That(latest!.Health).IsEqualTo(ServerHealth.Degraded);
        await Assert.That(latest.Reason).IsEqualTo("a port is unreachable");
    }

    [Test]
    public async Task GetLatest_refuses_a_rollup_for_a_non_owning_requester()
    {
        AgentId reporter = AgentId.New();
        ServerId server = ServerId.New();
        ServerHealthCache cache = new();
        cache.Record(new ServerLiveHealth(reporter, server, ServerHealth.Failed, "crash", At));

        await Assert.That(cache.GetLatest(server, AgentId.New())).IsNull();
    }
}
