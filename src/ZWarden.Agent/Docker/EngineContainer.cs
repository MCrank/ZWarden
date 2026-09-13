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
/// <param name="State">The container's state string (e.g. <c>running</c>).</param>
/// <param name="Ports">The published port mappings.</param>
public sealed record EngineContainer(
    string Id,
    IReadOnlyDictionary<string, string> Labels,
    string State,
    IReadOnlyList<PublishedPort> Ports);
