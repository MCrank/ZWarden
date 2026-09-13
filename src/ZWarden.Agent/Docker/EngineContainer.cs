namespace ZWarden.Agent.Docker;

/// <summary>
/// A published host↔container port mapping read back from Docker discovery, used to work out which
/// two-port strides are already occupied on the host.
/// </summary>
/// <param name="HostPort">The host-side port.</param>
/// <param name="ContainerPort">The container-internal port (e.g. 16261).</param>
/// <param name="Protocol">The lowercase protocol, e.g. <c>udp</c>.</param>
public readonly record struct PublishedPort(ushort HostPort, ushort ContainerPort, string Protocol);

/// <summary>
/// The mechanical, undecorated view of a container as the Docker engine returns it — id, labels, state and
/// published ports, with no ZWarden interpretation applied. <see cref="IDockerEngine"/> produces these; the
/// domain logic (<see cref="ContainerRuntime"/>) recognises, filters and enforces on top of them. Keeping this
/// layer free of domain rules is what makes the runtime unit-testable without a live daemon.
/// </summary>
/// <param name="Id">The Docker container id.</param>
/// <param name="Labels">The container's labels (possibly empty; never null here).</param>
/// <param name="State">The container's state string (e.g. <c>running</c>, <c>exited</c>, <c>dead</c>).</param>
/// <param name="Ports">The published port mappings.</param>
/// <param name="HealthStatus">The container's own HEALTHCHECK verdict from inspect <c>State.Health.Status</c>
/// (<c>healthy</c>/<c>unhealthy</c>/<c>starting</c>), or <c>null</c> when the image declares no healthcheck or
/// the list API (which does not carry it) produced this record. F16's process/startup probes read it.</param>
/// <param name="ExitCode">The last exit code from inspect <c>State.ExitCode</c> (0 unless known). Distinguishes a
/// clean stop from a crash in the health rollup.</param>
/// <param name="OomKilled">Whether the container was OOM-killed (inspect <c>State.OOMKilled</c>).</param>
public sealed record EngineContainer(
    string Id,
    IReadOnlyDictionary<string, string> Labels,
    string State,
    IReadOnlyList<PublishedPort> Ports,
    string? HealthStatus = null,
    long ExitCode = 0,
    bool OomKilled = false);
