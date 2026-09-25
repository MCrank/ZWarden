using ZWarden.Application.Servers;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Servers;

namespace ZWarden.Infrastructure.Tests.Servers;

/// <summary>
/// #230: the host capacity the wizard reads — the latest report per Agent, and the arithmetic behind "X GiB free for
/// new servers" and the overcommit warning (free = total − committed − reserve; a server commits heap + overhead).
/// </summary>
public class HostCapacityCacheTests
{
    private const long GiB = 1024L * 1024 * 1024;
    private static readonly DateTimeOffset At = new(2026, 9, 25, 21, 0, 0, TimeSpan.Zero);

    private static HostCapacity Capacity(AgentId agent, long total = 32 * GiB, long committed = 10 * GiB) =>
        new(agent, total, committed, OverheadBytes: 6 * GiB, DefaultHeapBytes: 4 * GiB, ReserveBytes: 2 * GiB, At);

    [Test]
    public async Task The_latest_report_per_agent_wins_and_an_unknown_agent_has_none()
    {
        AgentId agent = AgentId.New();
        HostCapacityCache cache = new();

        cache.Record(Capacity(agent, committed: 10 * GiB));
        cache.Record(Capacity(agent, committed: 16 * GiB));

        await Assert.That(cache.GetLatest(agent)!.CommittedBytes).IsEqualTo(16 * GiB);
        await Assert.That(cache.GetLatest(AgentId.New())).IsNull();
    }

    [Test]
    public async Task Free_memory_is_total_less_committed_less_reserve()
    {
        await Assert.That(Capacity(AgentId.New(), total: 32 * GiB, committed: 10 * GiB).FreeBytes).IsEqualTo(20 * GiB);
    }

    [Test]
    public async Task Free_memory_never_goes_negative_on_an_overcommitted_host()
    {
        await Assert.That(Capacity(AgentId.New(), total: 16 * GiB, committed: 20 * GiB).FreeBytes).IsEqualTo(0);
    }

    [Test]
    public async Task A_new_server_commits_its_heap_plus_the_overhead_and_the_shortfall_is_what_does_not_fit()
    {
        HostCapacity capacity = Capacity(AgentId.New(), total: 32 * GiB, committed: 10 * GiB); // 20 GiB free

        await Assert.That(capacity.LimitFor(8 * GiB)).IsEqualTo(14 * GiB);
        await Assert.That(capacity.ShortfallFor(8 * GiB)).IsEqualTo(0);
        await Assert.That(capacity.ShortfallFor(16 * GiB)).IsEqualTo(2 * GiB);
    }
}
