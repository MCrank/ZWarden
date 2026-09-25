using ZWarden.Application.Servers;
using ZWarden.Domain.Ids;
using ZWarden.Web.Components.Servers;

namespace ZWarden.Web.Tests.Servers;

/// <summary>
/// #230: the new-server wizard's parsing and wording, shared by the /servers form and the Server Detail recreate form.
/// The heap is typed in GiB; blank uses the suggestion for the expected player count (D2), or the Agent's default when
/// that is blank too. The capacity line and the shortfall warning are what the operator reads before acknowledging (D1).
/// </summary>
public class NewServerFormTests
{
    private const long GiB = 1024L * 1024 * 1024;

    [Test]
    [Arguments("6", 6 * GiB)]
    [Arguments("6.5", (6 * GiB) + (GiB / 2))]
    [Arguments(" 12 ", 12 * GiB)]
    public async Task A_typed_heap_in_GiB_wins(string typed, long expected)
    {
        bool ok = NewServerForm.TryResolveHeap(typed, expectedPlayers: "32", out long? heap, out string? error);

        await Assert.That(ok).IsTrue();
        await Assert.That(error).IsNull();
        await Assert.That(heap).IsEqualTo(expected);
    }

    [Test]
    public async Task A_blank_heap_uses_the_suggestion_for_the_expected_players()
    {
        bool ok = NewServerForm.TryResolveHeap(null, expectedPlayers: "8", out long? heap, out _);

        await Assert.That(ok).IsTrue();
        await Assert.That(heap).IsEqualTo(6 * GiB);
    }

    [Test]
    public async Task Both_blank_leaves_the_heap_to_the_agent()
    {
        bool ok = NewServerForm.TryResolveHeap(" ", " ", out long? heap, out _);

        await Assert.That(ok).IsTrue();
        await Assert.That(heap).IsNull();
    }

    [Test]
    [Arguments("lots", null)]
    [Arguments("1", null)]
    [Arguments("-4", null)]
    [Arguments(null, "many")]
    [Arguments(null, "300")]
    public async Task An_unreadable_or_out_of_range_input_is_an_operator_facing_error(string? typedHeap, string? players)
    {
        bool ok = NewServerForm.TryResolveHeap(typedHeap, players, out long? heap, out string? error);

        await Assert.That(ok).IsFalse();
        await Assert.That(heap).IsNull();
        await Assert.That(error).IsNotNull();
    }

    [Test]
    public async Task Max_players_is_optional_and_bounded()
    {
        await Assert.That(NewServerForm.TryParseMaxPlayers(null, out int? none, out _)).IsTrue();
        await Assert.That(none).IsNull();
        await Assert.That(NewServerForm.TryParseMaxPlayers("16", out int? sixteen, out _)).IsTrue();
        await Assert.That(sixteen).IsEqualTo(16);
        await Assert.That(NewServerForm.TryParseMaxPlayers("0", out _, out string? error)).IsFalse();
        await Assert.That(error).IsNotNull();
    }

    [Test]
    [Arguments(6 * GiB, "6 GiB")]
    [Arguments((6 * GiB) + (GiB / 2), "6.5 GiB")]
    [Arguments(0L, "0 GiB")]
    public async Task Sizes_read_as_GiB(long bytes, string expected)
    {
        await Assert.That(NewServerForm.FormatGiB(bytes)).IsEqualTo(expected);
    }

    [Test]
    public async Task The_capacity_line_says_what_is_free_and_what_is_kept_for_the_host()
    {
        HostCapacity capacity = new(AgentId.New(), 32 * GiB, 10 * GiB, 6 * GiB, 4 * GiB, 2 * GiB, DateTimeOffset.UtcNow);

        await Assert.That(NewServerForm.CapacityLine(capacity))
            .IsEqualTo("20 GiB free for new servers of 32 GiB (keeping 2 GiB for the host).");
        await Assert.That(NewServerForm.CapacityLine(null)).Contains("not reported");
    }

    [Test]
    public async Task The_shortfall_warning_names_the_limit_and_how_far_it_overshoots()
    {
        HostCapacity capacity = new(AgentId.New(), 16 * GiB, 10 * GiB, 6 * GiB, 4 * GiB, 2 * GiB, DateTimeOffset.UtcNow);

        string warning = NewServerForm.ShortfallWarning(capacity, 4 * GiB);

        await Assert.That(warning).Contains("10 GiB");
        await Assert.That(warning).Contains("4 GiB free");
        await Assert.That(warning).Contains("6 GiB");
    }
}
