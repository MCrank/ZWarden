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

    [Test]
    public async Task Read_of_an_unseen_server_is_an_empty_undropped_slice()
    {
        ServerLogBuffer buffer = Buffer();

        ServerLogSlice slice = buffer.Read(ServerId.New(), AgentId.New(), afterSequence: 0);

        await Assert.That(slice.Lines).IsEmpty();
        await Assert.That(slice.Dropped).IsFalse();
    }
}
