using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.Docker;
using ZWarden.Agent.LogStreaming;
using ZWarden.Agent.Tests.Docker;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests.LogStreaming;

/// <summary>
/// F27: the on-demand log subscription service over a fake runtime and a recording emitter. It proves lines are
/// sanitized, sequenced and stream-tagged; follows are deduped; the per-second cap drops and flags; a stop tears
/// the follow down; and a Server with no container yet is retried (so watching a stopped Server resumes).
/// </summary>
public class ServerLogSubscriptionServiceTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private static AgentOptions FastOptions() => new()
    {
        LogBatchFlushInterval = TimeSpan.FromMilliseconds(20),
        LogFollowRetryInterval = TimeSpan.FromMilliseconds(30),
    };

    private static ServerLogSubscriptionService Service(FakeContainerRuntime runtime, AgentOptions? options = null) =>
        new(
            runtime,
            Options.Create(options ?? FastOptions()),
            TimeProvider.System,
            NullLogger<ServerLogSubscriptionService>.Instance);

    [Test]
    public async Task Start_streams_sanitized_lines_with_sequence_and_stream()
    {
        ServerId server = ServerId.New();
        var runtime = new FakeContainerRuntime { FollowBlocksUntilCancelled = true };
        runtime.FollowFrames.Add(new ContainerLogFrame(At, IsStderr: false, "\x1B[32mworld loaded\x1B[0m"));
        runtime.FollowFrames.Add(new ContainerLogFrame(At, IsStderr: true, "java exception"));
        var emitter = new RecordingEmitter();
        await using ServerLogSubscriptionService service = Service(runtime);

        service.Start(server, emitter);

        IReadOnlyList<ServerLogLine> lines = await WaitForLinesAsync(emitter, server, count: 2);

        string[] expectedText = ["world loaded", "java exception"];
        await Assert.That(lines.Select(l => l.Text)).IsEquivalentTo(expectedText);
        await Assert.That(lines[0].Sequence).IsEqualTo(1L);
        await Assert.That(lines[1].Sequence).IsEqualTo(2L);
        await Assert.That(lines[0].Stream).IsEqualTo(LogStreamKind.Stdout);
        await Assert.That(lines[1].Stream).IsEqualTo(LogStreamKind.Stderr);
        await Assert.That(runtime.FollowTailLines).IsEqualTo(new AgentOptions().LogTailLines);
    }

    [Test]
    public async Task Start_is_idempotent_and_does_not_open_a_second_follow()
    {
        ServerId server = ServerId.New();
        var runtime = new FakeContainerRuntime { FollowBlocksUntilCancelled = true };
        var emitter = new RecordingEmitter();
        await using ServerLogSubscriptionService service = Service(runtime);

        service.Start(server, emitter);
        service.Start(server, emitter);

        await WaitUntilAsync(() => runtime.FollowCount >= 1);
        await Task.Delay(100);
        await Assert.That(runtime.FollowCount).IsEqualTo(1);
    }

    [Test]
    public async Task Stop_ends_the_follow_and_no_further_batches_arrive()
    {
        ServerId server = ServerId.New();
        var runtime = new FakeContainerRuntime { FollowBlocksUntilCancelled = true };
        runtime.FollowFrames.Add(new ContainerLogFrame(At, IsStderr: false, "line"));
        var emitter = new RecordingEmitter();
        await using ServerLogSubscriptionService service = Service(runtime);

        service.Start(server, emitter);
        await WaitForLinesAsync(emitter, server, count: 1);

        await service.StopAsync(server);
        int batchesAfterStop = emitter.Batches.Length;
        await Task.Delay(150); // well over the flush interval

        await Assert.That(emitter.Batches.Length).IsEqualTo(batchesAfterStop);
    }

    [Test]
    public async Task Lines_over_the_per_second_cap_are_dropped_and_the_batch_is_flagged()
    {
        ServerId server = ServerId.New();
        var runtime = new FakeContainerRuntime { FollowBlocksUntilCancelled = true };
        runtime.FollowFrames.Add(new ContainerLogFrame(At, IsStderr: false, "kept"));
        runtime.FollowFrames.Add(new ContainerLogFrame(At, IsStderr: false, "dropped-1"));
        runtime.FollowFrames.Add(new ContainerLogFrame(At, IsStderr: false, "dropped-2"));
        AgentOptions options = FastOptions();
        options.LogMaxLinesPerSecond = 1;
        var emitter = new RecordingEmitter();
        await using ServerLogSubscriptionService service = Service(runtime, options);

        service.Start(server, emitter);

        await WaitUntilAsync(() => emitter.Batches.Any(b => b.ServerId == server && b.Dropped));
        List<ServerLogLine> lines = emitter.Batches.Where(b => b.ServerId == server).SelectMany(b => b.Lines).ToList();

        string[] expectedKept = ["kept"];
        await Assert.That(lines.Select(l => l.Text)).IsEquivalentTo(expectedKept);
    }

    [Test]
    public async Task A_follow_that_finds_no_container_is_retried()
    {
        ServerId server = ServerId.New();
        var runtime = new FakeContainerRuntime { FollowException = new ContainerNotFoundException(server) };
        var emitter = new RecordingEmitter();
        await using ServerLogSubscriptionService service = Service(runtime);

        service.Start(server, emitter);

        await WaitUntilAsync(() => runtime.FollowCount >= 2);
        await Assert.That(runtime.FollowCount).IsGreaterThanOrEqualTo(2);
    }

    private static async Task<IReadOnlyList<ServerLogLine>> WaitForLinesAsync(RecordingEmitter emitter, ServerId server, int count)
    {
        await WaitUntilAsync(() => emitter.Batches.Where(b => b.ServerId == server).Sum(b => b.Lines.Count) >= count);
        return emitter.Batches.Where(b => b.ServerId == server).SelectMany(b => b.Lines).ToList();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("The awaited condition was not met within the timeout.");
    }

    private sealed record RecordedBatch(ServerId ServerId, IReadOnlyList<ServerLogLine> Lines, bool Dropped);

    private sealed class RecordingEmitter : IServerLogEmitter
    {
        private readonly ConcurrentQueue<RecordedBatch> _batches = new();

        public RecordedBatch[] Batches => _batches.ToArray();

        public Task EmitAsync(ServerId serverId, IReadOnlyList<ServerLogLine> lines, bool dropped, CancellationToken cancellationToken)
        {
            _batches.Enqueue(new RecordedBatch(serverId, lines, dropped));
            return Task.CompletedTask;
        }
    }
}
