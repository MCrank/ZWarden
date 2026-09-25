using ZWarden.Domain.Servers;

namespace ZWarden.Domain.Tests.Servers;

/// <summary>
/// #230: the per-server heap. The operator chooses only the heap (the limit is heap + the Agent's overhead), within
/// sane bounds and in whole MiB (the JVM flag is written in <c>m</c>); the wizard suggests one from the player count.
/// </summary>
public class ServerMemoryRulesTests
{
    private const long GiB = ServerMemoryRules.GiB;

    [Test]
    [Arguments(2 * GiB)]
    [Arguments(6 * GiB)]
    [Arguments((6 * GiB) + (GiB / 2))]
    [Arguments(128 * GiB)]
    public async Task A_heap_within_bounds_in_whole_MiB_is_valid(long heap)
    {
        await Assert.That(ServerMemoryRules.ValidateHeap(heap)).IsNull();
    }

    [Test]
    [Arguments(0L)]
    [Arguments(-1L)]
    [Arguments((2 * GiB) - ServerMemoryRules.MiB)]
    [Arguments((128 * GiB) + ServerMemoryRules.MiB)]
    public async Task A_heap_out_of_bounds_is_rejected(long heap)
    {
        await Assert.That(ServerMemoryRules.ValidateHeap(heap)).IsNotNull();
    }

    [Test]
    public async Task A_heap_that_is_not_a_whole_MiB_is_rejected()
    {
        await Assert.That(ServerMemoryRules.ValidateHeap((4 * GiB) + 1)).IsNotNull();
    }

    [Test]
    [Arguments(0, 4 * GiB)]
    [Arguments(1, (4 * GiB) + (GiB / 2))]
    [Arguments(8, 6 * GiB)]
    [Arguments(9, (6 * GiB) + (GiB / 2))]
    [Arguments(16, 8 * GiB)]
    [Arguments(32, 12 * GiB)]
    public async Task The_suggestion_is_4_GiB_plus_a_quarter_GiB_per_player_rounded_up_to_half_a_GiB(int players, long expected)
    {
        await Assert.That(ServerMemoryRules.SuggestHeap(players)).IsEqualTo(expected);
    }

    [Test]
    public async Task The_suggestion_clamps_the_player_count_and_is_always_a_valid_heap()
    {
        await Assert.That(ServerMemoryRules.SuggestHeap(-5)).IsEqualTo(4 * GiB);
        await Assert.That(ServerMemoryRules.SuggestHeap(10_000)).IsEqualTo(ServerMemoryRules.SuggestHeap(254));
        await Assert.That(ServerMemoryRules.ValidateHeap(ServerMemoryRules.SuggestHeap(254))).IsNull();
    }
}
