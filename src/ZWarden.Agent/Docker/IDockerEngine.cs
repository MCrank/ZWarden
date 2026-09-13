using Docker.DotNet.Models;

namespace ZWarden.Agent.Docker;

/// <summary>
/// The thin, mechanical wrapper over the Docker Engine API (F13's "Docker API abstraction") — exactly the ten
/// allowlisted operations of ADR 0008 and nothing more. It carries <b>no</b> ZWarden policy: no label
/// recognition, no ownership enforcement, no create-body construction. Those live in
/// <see cref="ContainerRuntime"/>, which is why this seam exists — it lets the runtime's logic be unit-tested
/// against a fake engine while the real adapter (<see cref="DockerDotNetEngine"/>) is exercised only in the
/// integration tier. Every method maps to one allowlisted verb; there is deliberately no delete, exec, kill,
/// prune, image-pull, volume or network operation here, so the Agent cannot call one even by mistake.
/// </summary>
public interface IDockerEngine
{
    /// <summary>Pings the daemon and returns the negotiated Engine API version. Throws if unreachable.</summary>
    Task<string> PingApiVersionAsync(CancellationToken cancellationToken);

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
}
