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

    [Test]
    public async Task Resizing_a_server_counts_its_current_limit_as_released()
    {
        // 16 GiB host, 2 reserved, 20 committed (two default servers at 4+6) ⇒ 0 free for a NEW server, but raising one
        // of them releases its own 10 GiB first: 16 − 10 − 2 = 4 GiB free for it.
        HostCapacity capacity = Capacity(AgentId.New(), total: 16 * GiB, committed: 20 * GiB);

        await Assert.That(capacity.FreeForResize(4 * GiB)).IsEqualTo(4 * GiB);
        await Assert.That(capacity.ResizeShortfall(4 * GiB, 8 * GiB)).IsEqualTo(10 * GiB); // 14 limit − 4 free
        await Assert.That(capacity.ResizeShortfall(null, 8 * GiB)).IsEqualTo(10 * GiB);   // unknown ⇒ the default heap
    }

    [Test]
    public async Task Keeping_or_lowering_the_heap_is_never_short_even_on_an_overcommitted_host()
    {
        HostCapacity capacity = Capacity(AgentId.New(), total: 16 * GiB, committed: 30 * GiB);

        await Assert.That(capacity.ResizeShortfall(8 * GiB, 8 * GiB)).IsEqualTo(0);
        await Assert.That(capacity.ResizeShortfall(8 * GiB, 4 * GiB)).IsEqualTo(0);
    }

    [Test]
    public async Task Raising_the_heap_within_the_freed_room_is_not_short()
    {
        HostCapacity capacity = Capacity(AgentId.New(), total: 32 * GiB, committed: 10 * GiB); // 20 free + own 10 back

        await Assert.That(capacity.ResizeShortfall(4 * GiB, 20 * GiB)).IsEqualTo(0);  // 26 limit ≤ 30
        await Assert.That(capacity.ResizeShortfall(4 * GiB, 26 * GiB)).IsEqualTo(2 * GiB);
    }
}
