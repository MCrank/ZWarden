using ZWarden.Application.Servers;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Servers;

namespace ZWarden.Infrastructure.Tests.Servers;

/// <summary>
/// F27: the bounded per-(Server, reporting Agent) log tail. These pin the cursor read, the ownership-partition
/// guard (a foreign Agent's batch never surfaces to the true owner), the fixed-size eviction, and the sticky
/// dropped flag.
/// </summary>
public class ServerLogBufferTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] BandC = ["b", "c"];
    private static readonly string[] JustReal = ["real"];
    private static readonly string[] DandE = ["d", "e"];
    private static readonly string[] AtoD = ["a", "b", "c", "d"];
    private static readonly string[] AandB = ["a", "b"];
    private static readonly long[] OneTwoThree = [1, 2, 3];

    private static ServerLogLineView Line(long sequence, string text) =>
        new(sequence, At, IsStderr: false, text, Truncated: false);

    private static ServerLogBuffer Buffer(int max = 2000) => new(new ServerLogBufferOptions { MaxLinesPerServer = max });

    [Test]
    public async Task Read_returns_only_lines_after_the_cursor()
    {
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();
        ServerLogBuffer buffer = Buffer();
        buffer.Append(agent, server, [Line(1, "a"), Line(2, "b"), Line(3, "c")], dropped: false);

        ServerLogSlice slice = buffer.Read(server, agent, afterSequence: 1);

        await Assert.That(slice.Lines.Select(l => l.Text)).IsEquivalentTo(BandC);
    }

    [Test]
    public async Task Read_from_zero_returns_the_whole_retained_tail()
    {
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();
        ServerLogBuffer buffer = Buffer();
        buffer.Append(agent, server, [Line(1, "a"), Line(2, "b")], dropped: false);

        ServerLogSlice slice = buffer.Read(server, agent, afterSequence: 0);

        await Assert.That(slice.Lines).Count().IsEqualTo(2);
    }

    [Test]
    public async Task A_batch_from_a_foreign_agent_never_surfaces_to_the_true_owner()
    {
        AgentId reporter = AgentId.New();
        AgentId trueOwner = AgentId.New();
        ServerId server = ServerId.New();
        ServerLogBuffer buffer = Buffer();
        buffer.Append(reporter, server, [Line(1, "forged")], dropped: false);

        ServerLogSlice slice = buffer.Read(server, trueOwner, afterSequence: 0);

        await Assert.That(slice.Lines).IsEmpty();
    }

    [Test]
    public async Task A_foreign_batch_does_not_shadow_the_owners_own_lines()
    {
        AgentId owner = AgentId.New();
        AgentId foreign = AgentId.New();
        ServerId server = ServerId.New();
        ServerLogBuffer buffer = Buffer();
        buffer.Append(owner, server, [Line(1, "real")], dropped: false);
        buffer.Append(foreign, server, [Line(2, "forged")], dropped: false);

        ServerLogSlice slice = buffer.Read(server, owner, afterSequence: 0);

        await Assert.That(slice.Lines.Select(l => l.Text)).IsEquivalentTo(JustReal);
    }

    [Test]
    public async Task The_tail_is_bounded_and_evicts_the_oldest_lines()
    {
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();
        ServerLogBuffer buffer = Buffer(max: 2);
        buffer.Append(agent, server, [Line(1, "a"), Line(2, "b"), Line(3, "c")], dropped: false);

        ServerLogSlice slice = buffer.Read(server, agent, afterSequence: 0);

        await Assert.That(slice.Lines.Select(l => l.Text)).IsEquivalentTo(BandC);
    }

    [Test]
    public async Task The_dropped_flag_is_sticky_once_a_batch_reports_it()
    {
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();
        ServerLogBuffer buffer = Buffer();
        buffer.Append(agent, server, [Line(1, "a")], dropped: true);
        buffer.Append(agent, server, [Line(2, "b")], dropped: false);

        ServerLogSlice slice = buffer.Read(server, agent, afterSequence: 0);

        await Assert.That(slice.Dropped).IsTrue();
    }

    private static ServerLogLineView LineAt(long agentSequence, int second, string text) =>
        new(agentSequence, At.AddSeconds(second), IsStderr: false, text, Truncated: false);

    [Test]
    public async Task A_new_follow_restarting_the_agent_sequence_still_surfaces_new_lines()
    {
        // #241: each follow on the Agent numbers its lines from 1 again (the viewer left and came back). The panel's
        // cursor sits at the old follow's last line; the new follow's lines must still read as newer.
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();
        ServerLogBuffer buffer = Buffer();
        buffer.Append(agent, server, [LineAt(1, 0, "a"), LineAt(2, 1, "b"), LineAt(3, 2, "c")], dropped: false);
        long cursor = buffer.Read(server, agent, afterSequence: 0).Lines[^1].Sequence;

        buffer.Append(agent, server, [LineAt(1, 3, "d"), LineAt(2, 4, "e")], dropped: false);
        ServerLogSlice slice = buffer.Read(server, agent, cursor);

        await Assert.That(slice.Lines.Select(l => l.Text)).IsEquivalentTo(DandE);
    }

    [Test]
    public async Task A_replayed_tail_is_not_buffered_twice()
    {
        // #241: a new follow (or a re-attach after a container restart) re-sends the last N lines as its tail. Lines
        // at or before the newest buffered timestamp that were already kept are replays, not new output.
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();
        ServerLogBuffer buffer = Buffer();
        buffer.Append(agent, server, [LineAt(1, 0, "a"), LineAt(2, 1, "b"), LineAt(3, 2, "c")], dropped: false);

        buffer.Append(agent, server, [LineAt(1, 1, "b"), LineAt(2, 2, "c"), LineAt(3, 3, "d")], dropped: false);
        ServerLogSlice slice = buffer.Read(server, agent, afterSequence: 0);

        await Assert.That(slice.Lines.Select(l => l.Text)).IsEquivalentTo(AtoD);
    }

    [Test]
    public async Task Distinct_lines_sharing_the_newest_timestamp_are_all_kept()
    {
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();
        ServerLogBuffer buffer = Buffer();
        buffer.Append(agent, server, [LineAt(1, 0, "a")], dropped: false);

        buffer.Append(agent, server, [LineAt(2, 0, "b")], dropped: false);
        ServerLogSlice slice = buffer.Read(server, agent, afterSequence: 0);

        await Assert.That(slice.Lines.Select(l => l.Text)).IsEquivalentTo(AandB);
    }

    [Test]
    public async Task A_stderr_line_slightly_older_than_the_newest_stdout_line_is_kept()
    {
        // The daemon timestamps each stream on its own, so stdout and stderr can interleave out of timestamp order.
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();
        ServerLogBuffer buffer = Buffer();
        buffer.Append(agent, server, [LineAt(1, 1, "a")], dropped: false);

        buffer.Append(agent, server, [new ServerLogLineView(2, At, IsStderr: true, "b", Truncated: false)], dropped: false);
        ServerLogSlice slice = buffer.Read(server, agent, afterSequence: 0);

        await Assert.That(slice.Lines.Select(l => l.Text)).IsEquivalentTo(AandB);
    }

    [Test]
    public async Task The_read_cursor_sequence_strictly_increases_in_append_order()
    {
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();
        ServerLogBuffer buffer = Buffer();
        buffer.Append(agent, server, [LineAt(7, 0, "a"), LineAt(8, 1, "b")], dropped: false);
        buffer.Append(agent, server, [LineAt(1, 2, "c")], dropped: false);

        long[] sequences = buffer.Read(server, agent, afterSequence: 0).Lines.Select(l => l.Sequence).ToArray();

        await Assert.That(sequences).IsEquivalentTo(OneTwoThree);
    }

    [Test]
    public async Task Read_of_an_unseen_server_is_an_empty_undropped_slice()
    {
        ServerLogBuffer buffer = Buffer();

        ServerLogSlice slice = buffer.Read(ServerId.New(), AgentId.New(), afterSequence: 0);

        await Assert.That(slice.Lines).IsEmpty();
        await Assert.That(slice.Dropped).IsFalse();
    }
}
