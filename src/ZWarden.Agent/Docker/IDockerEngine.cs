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

    /// <summary>Creates a container from fully-formed parameters and returns its id.</summary>
    Task<string> CreateAsync(CreateContainerParameters parameters, CancellationToken cancellationToken);

    /// <summary>Starts a container by id.</summary>
    Task StartAsync(string containerId, CancellationToken cancellationToken);

    /// <summary>Stops a container by id.</summary>
    Task StopAsync(string containerId, CancellationToken cancellationToken);

    /// <summary>Restarts a container by id.</summary>
    Task RestartAsync(string containerId, CancellationToken cancellationToken);
}
