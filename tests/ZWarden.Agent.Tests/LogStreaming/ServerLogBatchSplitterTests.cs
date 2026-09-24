using System.Text.Json;
using ZWarden.Agent.LogStreaming;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests.LogStreaming;

/// <summary>
/// #232: a flush is split into envelopes that each serialize under the streamed-message budget, so a PZ start/stop
/// burst never approaches the hub's receive limit. Order is preserved, nothing is lost, and the "dropped" flag rides
/// only the first part.
/// </summary>
public class ServerLogBatchSplitterTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 4, 12, 51, TimeSpan.Zero);
    private static readonly ServerId Server = ServerId.New();

    [Test]
    public async Task A_burst_is_split_into_envelopes_that_each_fit_the_budget_in_order()
    {
        // 125 lines of HTML-sensitive text, which the protocol JSON escapes to six bytes a character.
        string text = string.Concat(Enumerable.Repeat("<Server> 'loading' & > ", 40));
        List<ServerLogLine> lines = [.. Enumerable.Range(1, 125).Select(i => Line(i, text))];

        IReadOnlyList<ServerLogBatch> parts = ServerLogBatchSplitter.Split(Server, lines, dropped: true);

        await Assert.That(parts.Count).IsGreaterThan(1);
        foreach (ServerLogBatch part in parts)
        {
            await Assert.That(EnvelopeBytes(part)).IsLessThanOrEqualTo(AgentHubProtocol.StreamedMessageBudgetBytes);
        }

        await Assert.That(string.Join(",", parts.SelectMany(p => p.Lines).Select(l => l.Sequence)))
            .IsEqualTo(string.Join(",", Enumerable.Range(1, 125)));
        await Assert.That(parts[0].Dropped).IsTrue();
        await Assert.That(parts.Skip(1).Any(p => p.Dropped)).IsFalse();
    }

    [Test]
    public async Task A_small_flush_stays_one_batch()
    {
        IReadOnlyList<ServerLogBatch> parts = ServerLogBatchSplitter.Split(Server, [Line(1, "hello"), Line(2, "world")], dropped: false);

        await Assert.That(parts.Count).IsEqualTo(1);
        await Assert.That(parts[0].Lines.Count).IsEqualTo(2);
    }

    [Test]
    public async Task A_drop_only_flush_still_sends_one_empty_batch_carrying_the_flag()
    {
        IReadOnlyList<ServerLogBatch> parts = ServerLogBatchSplitter.Split(Server, [], dropped: true);

        await Assert.That(parts.Count).IsEqualTo(1);
        await Assert.That(parts[0].Lines).IsEmpty();
        await Assert.That(parts[0].Dropped).IsTrue();
    }

    [Test]
    public async Task The_largest_possible_line_still_fits_one_envelope()
    {
        // A sanitized line is capped at LogLineMaxCharacters (2,000); fully escaped that is ~12 KB, under the budget.
        string worst = new('<', 2000);

        IReadOnlyList<ServerLogBatch> parts = ServerLogBatchSplitter.Split(Server, [Line(1, worst), Line(2, worst)], dropped: false);

        foreach (ServerLogBatch part in parts)
        {
            await Assert.That(EnvelopeBytes(part)).IsLessThanOrEqualTo(AgentHubProtocol.StreamedMessageBudgetBytes);
        }

        await Assert.That(parts.Sum(p => p.Lines.Count)).IsEqualTo(2);
    }

    private static ServerLogLine Line(long sequence, string text) =>
        new(sequence, Now, LogStreamKind.Stdout, text, Truncated: false);

    private static int EnvelopeBytes(ServerLogBatch batch) =>
        JsonSerializer.SerializeToUtf8Bytes(Envelope.Create(batch, Now, AgentId.New(), Server), ProtocolJson.Options).Length;
}
