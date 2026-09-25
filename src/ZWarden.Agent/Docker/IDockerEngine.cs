using Docker.DotNet.Models;

namespace ZWarden.Agent.Docker;

/// <summary>
/// The thin, mechanical wrapper over the Docker Engine API (F13's "Docker API abstraction") — exactly the ten
/// allowlisted operations of ADR 0008 and nothing more. It carries <b>no</b> ZWarden policy: no label
/// recognition, no ownership enforcement, no create-body construction. Those live in
/// <see cref="ContainerRuntime"/>, which is why this seam exists — it lets the runtime's logic be unit-tested
/// against a fake engine while the real adapter (<see cref="DockerDotNetEngine"/>) is exercised only in the
/// integration tier. Every method maps to one allowlisted verb; there is deliberately no forced delete, exec, kill,
/// prune, image-pull, volume or network operation here, so the Agent cannot call one even by mistake. The one delete (#229,
/// ADR 0045) removes a stopped container by its UUID name only.
/// </summary>
public interface IDockerEngine
{
    /// <summary>Pings the daemon and returns the negotiated Engine API version. Throws if unreachable.</summary>
    Task<string> PingApiVersionAsync(CancellationToken cancellationToken);

    /// <summary>The Docker host's total RAM in bytes (<c>GET /info</c> <c>MemTotal</c>, an allowlisted read — ADR 0008).
    /// It is the daemon's host (or Docker Desktop VM) memory, so it is right however the Agent itself is
    /// containerised (#230).</summary>
    Task<long> TotalMemoryBytesAsync(CancellationToken cancellationToken);

    /// <summary>Lists all containers on the host (running and stopped), undecorated.</summary>
    Task<IReadOnlyList<EngineContainer>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Inspects one container by id, undecorated. Throws if it does not exist.</summary>
    Task<EngineContainer> InspectAsync(string containerId, CancellationToken cancellationToken);

    /// <summary>
    /// Reads a single, <b>non-streaming</b> resource-stats snapshot for a container (F16). Maps to the eleventh
    /// allowlist entry <c>GET /containers/{id}/stats?stream=false</c> (ADR 0008, amended by F16) — a read verb,
    /// no mutation. The raw counters feed <see cref="ContainerStatsCalculator"/>.
    /// </summary>
    Task<ContainerStatsSnapshot> StatsAsync(string containerId, CancellationToken cancellationToken);

    /// <summary>
    /// Reads a container's logs (stdout+stderr, <b>non-following</b>) since an optional time (F17). An optional
    /// <c>until</c> bounds the window from above (#262 reads only the first minutes after a start, so a long-running
    /// container never streams its whole history). Maps to the
    /// allowlisted read verb <c>GET /containers/{id}/logs</c> (ADR 0008) — a read verb, no mutation. Frames are
    /// returned in chronological order, so a caller can parse a banner-bracketed session out of the combined
    /// stream (SteamCMD tees its output to stderr; the entrypoint's session banners are on stdout).
    /// </summary>
    Task<string> ReadLogsAsync(
        string containerId, DateTimeOffset? since, DateTimeOffset? until, CancellationToken cancellationToken);

    /// <summary>
    /// Follows a container's logs (stdout+stderr) as a long-lived stream (F27), invoking <paramref name="onFrame"/>
    /// once per newline-delimited line as it arrives. It backfills the last <paramref name="tailLines"/> lines
    /// first, then streams live until the container exits, the stream ends, or <paramref name="cancellationToken"/>
    /// is cancelled — cancellation is the normal way a subscription is torn down and completes the task cleanly.
    /// Maps to the allowlisted read verb <c>GET /containers/{id}/logs?follow=1</c> (ADR 0008) — a read verb, no
    /// mutation, and the query string the proxy does not constrain, so streaming rides the same allowlist entry as
    /// <see cref="ReadLogsAsync"/>. Frames are raw and <b>unsanitized</b>; the caller sanitizes them (PRD 38). This
    /// is the streaming sibling of <see cref="ReadLogsAsync"/>, distinguished by carrying the stdout/stderr origin.
    /// </summary>
    Task FollowLogsAsync(
        string containerId,
        int tailLines,
        Func<ContainerLogFrame, CancellationToken, ValueTask> onFrame,
        CancellationToken cancellationToken);

    /// <summary>Creates a container from fully-formed parameters and returns its id.</summary>
    Task<string> CreateAsync(CreateContainerParameters parameters, CancellationToken cancellationToken);

    /// <summary>Starts a container by id.</summary>
    Task StartAsync(string containerId, CancellationToken cancellationToken);

    /// <summary>Stops a container by id, waiting <paramref name="waitBeforeKillSeconds"/> for a graceful exit
    /// before Docker SIGKILLs it (the <c>t</c> parameter of <c>POST /containers/{id}/stop</c>). The Agent sizes
    /// this above the image's save grace so the entrypoint's SIGTERM→FIFO <c>save</c>→<c>quit</c> completes
    /// (F15).</summary>
    Task StopAsync(string containerId, int waitBeforeKillSeconds, CancellationToken cancellationToken);

    /// <summary>Restarts a container by id, giving its stop half <paramref name="waitBeforeKillSeconds"/> for a
    /// graceful exit — the same safe stop timeout as <see cref="StopAsync"/> (F15).</summary>
    Task RestartAsync(string containerId, int waitBeforeKillSeconds, CancellationToken cancellationToken);

    /// <summary>Removes a stopped container by <b>name</b> (#229, ADR 0045) — never forced and never removing volumes.
    /// The socket proxy admits a DELETE only for a UUID-shaped name, so addressing by the hex id would be refused.</summary>
    Task RemoveAsync(string containerName, CancellationToken cancellationToken);
}
