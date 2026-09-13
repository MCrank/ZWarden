using ZWarden.Application.Servers;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Servers;

namespace ZWarden.Infrastructure.Tests.Servers;

/// <summary>
/// F16 PR-B: the in-memory metrics cache keeps the latest sample per Server and enforces the ownership guard on
/// read — a sample is returned only to a caller naming the Agent that reported it, so a forged sample cannot
/// surface under a Server owned by someone else.
/// </summary>
public class ServerMetricsCacheTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 13, 10, 0, 0, TimeSpan.Zero);

    private static ServerMetrics Sample(AgentId agent, ServerId server, double cpu) =>
        new(agent, server, cpu, 1_000, 4_000, 500, 50_000, null, At);

    [Test]
    public async Task GetLatest_returns_the_last_recorded_sample_for_its_owning_agent()
    {
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();
        ServerMetricsCache cache = new();

        cache.Record([Sample(agent, server, 10)]);
        cache.Record([Sample(agent, server, 42)]); // last write wins

        ServerMetrics? latest = cache.GetLatest(server, agent);
        await Assert.That(latest).IsNotNull();
        await Assert.That(latest!.CpuPercent).IsEqualTo(42d);
    }

    [Test]
    public async Task GetLatest_refuses_a_sample_when_the_requesting_owner_does_not_match()
    {
        AgentId reporter = AgentId.New();
        AgentId trueOwner = AgentId.New();
        ServerId server = ServerId.New();
        ServerMetricsCache cache = new();
        cache.Record([Sample(reporter, server, 99)]);

        // A sample forged by a different Agent for this ServerId is not returned under the real owner.
        await Assert.That(cache.GetLatest(server, trueOwner)).IsNull();
    }

    [Test]
    public async Task GetLatest_is_null_for_an_unseen_server()
    {
        ServerMetricsCache cache = new();

        await Assert.That(cache.GetLatest(ServerId.New(), AgentId.New())).IsNull();
    }
}
