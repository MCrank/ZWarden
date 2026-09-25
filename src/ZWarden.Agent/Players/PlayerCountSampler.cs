using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.Docker;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Players;

/// <summary>A player count the Agent read over RCON (#257) and when it read it (UTC).</summary>
public readonly record struct PlayerCountReading(int Count, DateTimeOffset SampledAt);

/// <summary>The Agent's last-known player count per Server (#257), read by the 15s metrics sampler.</summary>
public interface IServerPlayerCounts
{
    /// <summary>The last successful reading for <paramref name="serverId"/>, or <c>null</c> when the Server is not
    /// running, its last sample failed, or it has not been sampled yet.</summary>
    PlayerCountReading? GetLatest(ServerId serverId);
}

/// <summary>
/// Samples each Running Server's player count over RCON on its own slow cadence (#257,
/// <see cref="AgentOptions.PlayerCountSampleInterval"/>) and keeps the last value for the metrics report, so the
/// fleet board shows players without any page viewer ever reaching the Agent or PZ. A Server is first sampled after
/// a small per-Server offset, so one host's servers are not all queried at once. It reuses the F19 roster
/// (<see cref="IPlayerAdministration.ListPlayersAsync"/>: F19 quoting/parsing, F18 password ownership). Best-effort:
/// a failed or timed-out sample clears the count and doubles the wait (capped), and never touches health. A Server
/// that is not running is never queried, and its count is dropped.
/// </summary>
public sealed class PlayerCountSampler : IServerPlayerCounts
{
    /// <summary>The widest first-sample offset; each Server's offset within it is derived from its id.</summary>
    internal static readonly TimeSpan MaxInitialOffset = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(30);

    private readonly IPlayerAdministration _players;
    private readonly TimeSpan _interval;
    private readonly TimeProvider _clock;
    private readonly Dictionary<ServerId, Schedule> _schedules = [];
    private readonly Lock _gate = new();

    public PlayerCountSampler(IPlayerAdministration players, IOptions<AgentOptions> options, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        _players = players;
        _interval = options.Value.PlayerCountSampleInterval;
        _clock = clock;
    }

    /// <inheritdoc />
    public PlayerCountReading? GetLatest(ServerId serverId)
    {
        lock (_gate)
        {
            return _schedules.TryGetValue(serverId, out Schedule? schedule) ? schedule.Reading : null;
        }
    }

    /// <summary>
    /// Samples every Running Server in <paramref name="managed"/> whose next sample is due, one at a time, and
    /// forgets any Server that is no longer running or managed. Only <paramref name="cancellationToken"/> (shutdown)
    /// escapes; every per-Server failure is absorbed.
    /// </summary>
    public async Task SampleDueAsync(IReadOnlyList<ManagedContainer> managed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(managed);

        List<ServerId> due = [];
        DateTimeOffset now = _clock.GetUtcNow();
        lock (_gate)
        {
            HashSet<ServerId> running = managed
                .Where(c => string.Equals(c.State, "running", StringComparison.OrdinalIgnoreCase))
                .Select(c => c.ServerId)
                .ToHashSet();

            foreach (ServerId gone in _schedules.Keys.Where(id => !running.Contains(id)).ToList())
            {
                _schedules.Remove(gone);
            }

            foreach (ServerId id in running)
            {
                if (!_schedules.TryGetValue(id, out Schedule? schedule))
                {
                    schedule = new Schedule { NextDueAt = now + InitialOffset(id) };
                    _schedules[id] = schedule;
                }

                if (schedule.NextDueAt <= now)
                {
                    due.Add(id);
                }
            }
        }

        foreach (ServerId id in due)
        {
            PlayerCountReading? reading = await TrySampleAsync(id, cancellationToken).ConfigureAwait(false);
            Record(id, reading);
        }
    }

    private async Task<PlayerCountReading?> TrySampleAsync(ServerId serverId, CancellationToken cancellationToken)
    {
        try
        {
            PlayerRosterResult roster = await _players.ListPlayersAsync(serverId, cancellationToken).ConfigureAwait(false);
            return roster.Count >= 0 ? new PlayerCountReading(roster.Count, _clock.GetUtcNow()) : null;
        }
        catch (PlayerCommandException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // An RCON-level timeout, not shutdown: a failed sample like any other.
            return null;
        }
    }

    private void Record(ServerId serverId, PlayerCountReading? reading)
    {
        DateTimeOffset now = _clock.GetUtcNow();
        lock (_gate)
        {
            if (!_schedules.TryGetValue(serverId, out Schedule? schedule))
            {
                return; // Forgotten meanwhile (stopped or removed).
            }

            schedule.Reading = reading;
            if (reading is null)
            {
                schedule.Failures++;
                double factor = Math.Pow(2, Math.Min(schedule.Failures - 1, 10));
                TimeSpan wait = TimeSpan.FromTicks((long)Math.Min(_interval.Ticks * factor, MaxBackoff.Ticks));
                schedule.NextDueAt = now + (wait > _interval ? wait : _interval);
            }
            else
            {
                schedule.Failures = 0;
                schedule.NextDueAt = now + _interval;
            }
        }
    }

    // A stable per-Server offset in [0, MaxInitialOffset), so a host's servers are spread without randomness.
    private static TimeSpan InitialOffset(ServerId serverId)
    {
        uint hash = unchecked((uint)serverId.Value.GetHashCode());
        return TimeSpan.FromTicks(hash % MaxInitialOffset.Ticks);
    }

    private sealed class Schedule
    {
        public DateTimeOffset NextDueAt { get; set; }

        public PlayerCountReading? Reading { get; set; }

        public int Failures { get; set; }
    }
}
