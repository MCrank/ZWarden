using ZWarden.Agent.ServerConfig;
using ZWarden.Contracts.Protocol;

namespace ZWarden.Agent.Tests.ServerConfig;

/// <summary>
/// F20c PR-D (ADR 0042): the Agent's transient, bounded buffer that reassembles an operator-authored whole-file
/// configuration edit staged over the transport channel before the <c>ConfigApplyRaw</c> Operation takes it. It
/// hands back the text exactly once, refuses a partial or expired set, and drops stages beyond its concurrency
/// bound so a flood cannot grow it without limit.
/// </summary>
public class ServerConfigRawEditStagingTests
{
    private const string Raw = "SandboxVars = {\n    Zombies = 2,\n}\n";

    [Test]
    public async Task A_fully_staged_edit_is_taken_once()
    {
        var staging = new ServerConfigRawEditStaging(TimeProvider.System);
        foreach (ServerConfigRawEditChunk chunk in ServerConfigRawEditCodec.Encode("corr-1", Raw))
        {
            staging.Accept(chunk);
        }

        await Assert.That(staging.TryTake("corr-1", out string? text)).IsTrue();
        await Assert.That(text).IsEqualTo(Raw);
        // One-shot: a redelivery of the Operation finds nothing.
        await Assert.That(staging.TryTake("corr-1", out _)).IsFalse();
    }

    [Test]
    public async Task A_partial_set_is_not_takeable()
    {
        var staging = new ServerConfigRawEditStaging(TimeProvider.System);
        // Declare a two-chunk set but stage only the first chunk.
        staging.Accept(new ServerConfigRawEditChunk("corr-2", ChunkIndex: 0, ChunkCount: 2, Chunk: "AA=="));

        await Assert.That(staging.TryTake("corr-2", out _)).IsFalse();
    }

    [Test]
    public async Task An_expired_stage_is_swept_and_not_takeable()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero));
        var staging = new ServerConfigRawEditStaging(clock);
        foreach (ServerConfigRawEditChunk chunk in ServerConfigRawEditCodec.Encode("corr-3", Raw))
        {
            staging.Accept(chunk);
        }

        clock.Advance(ServerConfigRawEditStaging.Expiry + TimeSpan.FromSeconds(1));

        await Assert.That(staging.TryTake("corr-3", out _)).IsFalse();
    }

    [Test]
    public async Task Stages_beyond_the_concurrency_bound_are_dropped()
    {
        var staging = new ServerConfigRawEditStaging(TimeProvider.System);
        // Fill the buffer with incomplete two-chunk stages (each holds one chunk, so none is takeable yet).
        for (int i = 0; i < ServerConfigRawEditStaging.MaxConcurrentStaged; i++)
        {
            staging.Accept(new ServerConfigRawEditChunk($"pending-{i}", 0, 2, "AA=="));
        }

        // A fresh single-chunk edit past the bound is dropped rather than growing the buffer.
        foreach (ServerConfigRawEditChunk chunk in ServerConfigRawEditCodec.Encode("corr-over", Raw))
        {
            staging.Accept(chunk);
        }

        await Assert.That(staging.TryTake("corr-over", out _)).IsFalse();
    }

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
