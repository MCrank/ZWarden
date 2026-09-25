using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.Docker;
using ZWarden.Agent.Players;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests.Players;

/// <summary>
/// #257: the Agent samples each Running server's player count over RCON on its own slow cadence (default 180s, a
/// small per-server offset so one host's servers are not all queried at once) and keeps the last value for the
/// 15s metrics report. A failure clears the count and backs off; a server that is not running is never queried.
/// </summary>
public class PlayerCountSamplerTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(180);

    private readonly SettableClock _clock = new(Start);
    private readonly FakePlayerAdministration _players = new() { Roster = new PlayerRosterResult(3, ["a", "b", "c"]) };

    private PlayerCountSampler Build() => new(
        _players,
        Options.Create(new AgentOptions { PlayerCountSampleInterval = Interval }),
        _clock);

    private static ManagedContainer Running(ServerId id) => new("c-" + id, id, "running");

    // The first sighting only schedules the server (after its per-server offset); the next sweep past the offset
    // window takes the first sample.
    private async Task FirstSampleAsync(PlayerCountSampler sampler, ServerId server)
    {
        await sampler.SampleDueAsync([Running(server)], CancellationToken.None);
        _clock.Advance(PlayerCountSampler.MaxInitialOffset);
        await sampler.SampleDueAsync([Running(server)], CancellationToken.None);
    }

    [Test]
    public async Task A_running_server_is_sampled_within_the_offset_window_and_the_count_is_kept()
    {
        ServerId server = ServerId.New();
        PlayerCountSampler sampler = Build();

        await FirstSampleAsync(sampler, server);

        PlayerCountReading? reading = sampler.GetLatest(server);
        await Assert.That(reading).IsNotNull();
        await Assert.That(reading!.Value.Count).IsEqualTo(3);
        await Assert.That(reading.Value.SampledAt).IsEqualTo(_clock.GetUtcNow());
    }

    [Test]
    public async Task A_sampled_server_is_not_queried_again_until_the_interval_elapses()
    {
        ServerId server = ServerId.New();
        PlayerCountSampler sampler = Build();
        await FirstSampleAsync(sampler, server);

        _clock.Advance(Interval - TimeSpan.FromSeconds(1));
        await sampler.SampleDueAsync([Running(server)], CancellationToken.None);
        await Assert.That(_players.CallCount).IsEqualTo(1);

        _clock.Advance(TimeSpan.FromSeconds(1));
        _players.Roster = new PlayerRosterResult(5, ["a", "b", "c", "d", "e"]);
        await sampler.SampleDueAsync([Running(server)], CancellationToken.None);
        await Assert.That(_players.CallCount).IsEqualTo(2);
        await Assert.That(sampler.GetLatest(server)!.Value.Count).IsEqualTo(5);
    }

    [Test]
    public async Task A_failed_sample_clears_the_count_and_backs_off()
    {
        ServerId server = ServerId.New();
        PlayerCountSampler sampler = Build();
        await FirstSampleAsync(sampler, server);

        _players.Throw = new PlayerCommandException("RCON unreachable");
        _clock.Advance(Interval);
        await sampler.SampleDueAsync([Running(server)], CancellationToken.None);
        await Assert.That(sampler.GetLatest(server)).IsNull();
        await Assert.That(_players.CallCount).IsEqualTo(2);

        // First failure: retry after one interval.
        _clock.Advance(Interval);
        await sampler.SampleDueAsync([Running(server)], CancellationToken.None);
        await Assert.That(_players.CallCount).IsEqualTo(3);

        // Second consecutive failure: the wait doubles, so one interval later is still too soon.
        _clock.Advance(Interval);
        await sampler.SampleDueAsync([Running(server)], CancellationToken.None);
        await Assert.That(_players.CallCount).IsEqualTo(3);

        _players.Throw = null;
        _clock.Advance(Interval);
        await sampler.SampleDueAsync([Running(server)], CancellationToken.None);
        await Assert.That(_players.CallCount).IsEqualTo(4);
        await Assert.That(sampler.GetLatest(server)!.Value.Count).IsEqualTo(3);
    }

    [Test]
    public async Task A_timed_out_sample_is_a_failure_not_a_shutdown()
    {
        ServerId server = ServerId.New();
        PlayerCountSampler sampler = Build();
        _players.Throw = new OperationCanceledException();

        await FirstSampleAsync(sampler, server);

        await Assert.That(sampler.GetLatest(server)).IsNull();
    }

    [Test]
    public async Task A_server_that_is_not_running_is_never_queried_and_its_count_is_dropped()
    {
        ServerId server = ServerId.New();
        PlayerCountSampler sampler = Build();
        await FirstSampleAsync(sampler, server);
        await Assert.That(sampler.GetLatest(server)).IsNotNull();

        _clock.Advance(Interval);
        await sampler.SampleDueAsync([new ManagedContainer("c", server, "exited")], CancellationToken.None);

        await Assert.That(_players.CallCount).IsEqualTo(1);
        await Assert.That(sampler.GetLatest(server)).IsNull();
    }

    [Test]
    public async Task A_server_no_longer_managed_is_forgotten()
    {
        ServerId server = ServerId.New();
        PlayerCountSampler sampler = Build();
        await FirstSampleAsync(sampler, server);

        await sampler.SampleDueAsync([], CancellationToken.None);

        await Assert.That(sampler.GetLatest(server)).IsNull();
    }

    [Test]
    public async Task Shutdown_cancellation_propagates()
    {
        ServerId server = ServerId.New();
        PlayerCountSampler sampler = Build();
        using CancellationTokenSource stopping = new();
        await stopping.CancelAsync();
        _players.Throw = new OperationCanceledException(stopping.Token);
        await sampler.SampleDueAsync([Running(server)], stopping.Token); // first sighting: schedules only
        _clock.Advance(PlayerCountSampler.MaxInitialOffset);

        await Assert.That(async () => await sampler.SampleDueAsync([Running(server)], stopping.Token))
            .Throws<OperationCanceledException>();
    }

    private sealed class SettableClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
