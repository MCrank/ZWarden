using ZWarden.Application.Console;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Console;

namespace ZWarden.Infrastructure.Tests.Console;

/// <summary>
/// F28: the in-memory console output cache. It records the newest outputs per Server for the live pane, hands them
/// back after a sequence cursor (oldest first), enforces the ownership guard (only the reporting Agent's entries
/// are visible — §8), and drops the oldest beyond its per-Server bound. Transient display data — never persisted.
/// </summary>
public class ConsoleOutputCacheTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 15, 11, 0, 0, TimeSpan.Zero);
    private static readonly string[] OneThenTwo = ["one", "two"];

    [Test]
    public async Task Records_and_returns_an_output_for_the_owning_agent()
    {
        ConsoleOutputCache cache = new();
        ServerId server = ServerId.New();
        AgentId owner = AgentId.New();
        OperationId op = OperationId.New();

        cache.Record(server, owner, op, "Players connected (0): ", truncated: false, At);
        IReadOnlyList<ConsoleOutputEntry> entries = cache.GetSince(server, owner, afterSequence: 0);

        await Assert.That(entries.Count).IsEqualTo(1);
        await Assert.That(entries[0].Output).IsEqualTo("Players connected (0): ");
        await Assert.That(entries[0].OperationId).IsEqualTo(op);
        await Assert.That(entries[0].Truncated).IsFalse();
    }

    [Test]
    public async Task A_foreign_agent_cannot_read_another_agents_output()
    {
        ConsoleOutputCache cache = new();
        ServerId server = ServerId.New();
        AgentId owner = AgentId.New();

        cache.Record(server, owner, OperationId.New(), "secret output", truncated: false, At);
        IReadOnlyList<ConsoleOutputEntry> entries = cache.GetSince(server, AgentId.New(), afterSequence: 0);

        await Assert.That(entries).IsEmpty();
    }

    [Test]
    public async Task GetSince_returns_only_entries_after_the_cursor_oldest_first()
    {
        ConsoleOutputCache cache = new();
        ServerId server = ServerId.New();
        AgentId owner = AgentId.New();

        cache.Record(server, owner, OperationId.New(), "one", truncated: false, At);
        cache.Record(server, owner, OperationId.New(), "two", truncated: false, At);

        IReadOnlyList<ConsoleOutputEntry> all = cache.GetSince(server, owner, afterSequence: 0);
        await Assert.That(all.Select(e => e.Output)).IsEquivalentTo(OneThenTwo);
        await Assert.That(all[0].Sequence).IsLessThan(all[1].Sequence);

        // Poll again from the last seen cursor: only the newer entry comes back.
        long cursor = all[0].Sequence;
        IReadOnlyList<ConsoleOutputEntry> after = cache.GetSince(server, owner, cursor);
        await Assert.That(after.Count).IsEqualTo(1);
        await Assert.That(after[0].Output).IsEqualTo("two");
    }

    [Test]
    public async Task An_unknown_server_returns_empty()
    {
        ConsoleOutputCache cache = new();

        await Assert.That(cache.GetSince(ServerId.New(), AgentId.New(), afterSequence: 0)).IsEmpty();
    }

    [Test]
    public async Task The_cache_is_bounded_and_drops_the_oldest_entries()
    {
        ConsoleOutputCache cache = new();
        ServerId server = ServerId.New();
        AgentId owner = AgentId.New();

        for (int i = 0; i < ConsoleOutputCache.MaxEntriesPerServer + 10; i++)
        {
            cache.Record(server, owner, OperationId.New(), $"line-{i}", truncated: false, At);
        }

        IReadOnlyList<ConsoleOutputEntry> entries = cache.GetSince(server, owner, afterSequence: 0);
        await Assert.That(entries.Count).IsEqualTo(ConsoleOutputCache.MaxEntriesPerServer);
        // The oldest ten were dropped, so the first retained entry is line-10.
        await Assert.That(entries[0].Output).IsEqualTo("line-10");
    }
}
