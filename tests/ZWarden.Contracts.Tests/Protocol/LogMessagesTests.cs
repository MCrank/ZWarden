using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Contracts.Tests.Protocol;

/// <summary>
/// F27: the live-logs wire surface. <see cref="ServerLogBatch"/> is an additive <see cref="AgentEvent"/> leaf
/// carrying sanitized <see cref="ServerLogLine"/>s; it round-trips through the one canonical
/// <see cref="ProtocolJson"/>, realises the reserved <c>LogEntry → F27</c> slot, and does not bump the protocol
/// version. The subscription-control names it rides alongside are transport plumbing, not messages.
/// </summary>
public class LogMessagesTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task LogBatch_round_trips_its_lines_with_stream_and_flags()
    {
        ServerId server = ServerId.New();
        ServerLogBatch batch = new(
            server,
            [
                new ServerLogLine(1, At, LogStreamKind.Stdout, "world loaded", Truncated: false),
                new ServerLogLine(2, At, LogStreamKind.Stderr, "a very long line", Truncated: true),
            ],
            Dropped: true);
        Envelope<ServerLogBatch> original = Envelope.Create(batch, At, agentId: AgentId.New(), serverId: server);

        Envelope<ServerLogBatch> back = ProtocolJson.Deserialize<ServerLogBatch>(ProtocolJson.Serialize(original));

        // Records with a list member don't get value equality on the list, so compare the lines element-wise.
        await Assert.That(back.Payload.Lines).IsEquivalentTo(original.Payload.Lines);
        await Assert.That(back.Payload.ServerId).IsEqualTo(server);
        await Assert.That(back.Payload.Dropped).IsTrue();
        await Assert.That(back.Payload.Lines[1].Stream).IsEqualTo(LogStreamKind.Stderr);
        await Assert.That(back.Payload.Lines[1].Truncated).IsTrue();
    }

    [Test]
    public async Task LogBatch_declares_its_discriminator_and_stays_additive()
    {
        string json = ProtocolJson.Serialize(Envelope.Create(new ServerLogBatch(ServerId.New(), [], Dropped: false), At));

        await Assert.That(json).Contains("server.log-batch");
        await Assert.That(ProtocolJson.MessageTypes.ContainsKey("server.log-batch")).IsTrue();
        await Assert.That(ProtocolVersionRange.Supported.Current).IsEqualTo(1);
    }

    [Test]
    public async Task LogBatch_is_an_agent_event_never_a_command()
    {
        // A log batch is an observed report (Agent → Web), so it must be an AgentEvent — never an AgentCommand,
        // which is the operation-dispatch vocabulary. Its subscription control rides transport constants, not a
        // message type, so it never enters the closed command vocabulary at all.
        await Assert.That(typeof(AgentEvent).IsAssignableFrom(typeof(ServerLogBatch))).IsTrue();
        await Assert.That(typeof(AgentCommand).IsAssignableFrom(typeof(ServerLogBatch))).IsFalse();
    }
}
