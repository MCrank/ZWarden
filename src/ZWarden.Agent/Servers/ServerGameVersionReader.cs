using Docker.DotNet;
using ZWarden.Agent.Docker;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Servers;

/// <summary>The Agent's last-known Project Zomboid game version per Server (#262), read by the metrics sampler.</summary>
public interface IServerGameVersions
{
    /// <summary>
    /// The game version for <paramref name="container"/>'s Server. For a running container
    /// (<paramref name="startedAt"/> set) it reads the boot log of that start once, retrying while the server is
    /// still booting; a stopped container (<c>null</c>) keeps the last version seen. <c>null</c> when unknown.
    /// </summary>
    Task<string?> ReadAsync(ManagedContainer container, DateTimeOffset? startedAt, CancellationToken cancellationToken);

    /// <summary>Forgets every Server not in <paramref name="managed"/> (removed or no longer owned).</summary>
    void Retain(IEnumerable<ServerId> managed);
}

/// <summary>
/// The default <see cref="IServerGameVersions"/> (#262). PZ prints its version once, early in boot
/// (<see cref="PzGameVersionParser"/>), so each container start is read through the allowlisted, non-following
/// Docker log read bounded to <see cref="BootWindow"/> after the start: a long-running container never streams its
/// whole history, and a restart after an update re-reads it. Until the line appears it retries on each call (the
/// metrics cadence); once the window has passed without it, it gives up until the next start. Best-effort: a Docker
/// error is <c>null</c> for that call and never affects health. The container id comes from the owned-container
/// list, so no foreign container's logs are read.
/// </summary>
public sealed class ServerGameVersionReader : IServerGameVersions
{
    /// <summary>How far after a container start the boot line is looked for.</summary>
    internal static readonly TimeSpan BootWindow = TimeSpan.FromMinutes(10);

    // Docker's log `since` has one-second resolution; start a little early so the first lines are never cut off.
    private static readonly TimeSpan SinceSlack = TimeSpan.FromSeconds(5);

    private readonly IDockerEngine _engine;
    private readonly TimeProvider _clock;
    private readonly Dictionary<ServerId, Entry> _entries = [];
    private readonly Lock _gate = new();

    public ServerGameVersionReader(IDockerEngine engine, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(clock);
        _engine = engine;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<string?> ReadAsync(
        ManagedContainer container, DateTimeOffset? startedAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(container);
        Entry? entry;
        lock (_gate)
        {
            _entries.TryGetValue(container.ServerId, out entry);
        }

        if (startedAt is not { } started)
        {
            return entry?.Version; // Not running: keep the last version seen.
        }

        if (entry is not null && entry.StartedAt == started && (entry.Found || entry.GaveUp))
        {
            return entry.Version;
        }

        string? previous = entry?.Version;
        string? found;
        try
        {
            string log = await _engine.ReadLogsAsync(
                container.DockerId, started - SinceSlack, started + BootWindow, cancellationToken).ConfigureAwait(false);
            found = PzGameVersionParser.Parse(log);
        }
        catch (DockerApiException)
        {
            return previous; // Vanished or refused this call: try again next time.
        }

        bool gaveUp = found is null && _clock.GetUtcNow() > started + BootWindow;
        Entry updated = new(started, found ?? previous, found is not null, gaveUp);
        lock (_gate)
        {
            _entries[container.ServerId] = updated;
        }

        return updated.Version;
    }

    /// <inheritdoc />
    public void Retain(IEnumerable<ServerId> managed)
    {
        ArgumentNullException.ThrowIfNull(managed);
        HashSet<ServerId> keep = managed.ToHashSet();
        lock (_gate)
        {
            foreach (ServerId gone in _entries.Keys.Where(id => !keep.Contains(id)).ToList())
            {
                _entries.Remove(gone);
            }
        }
    }

    // Version is the latest known (it survives a restart until the new start's line is read); Found/GaveUp are
    // about the StartedAt this entry was read for.
    private sealed record Entry(DateTimeOffset StartedAt, string? Version, bool Found, bool GaveUp);
}
