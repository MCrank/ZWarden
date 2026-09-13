using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Docker;

/// <summary>
/// The Agent's domain-facing Docker runtime (F13): discovery, health, creation and lifecycle for canonical
/// ZWarden.PZServer containers this Agent owns — and refusal for everything else. Every target-container
/// operation passes the allowed-container enforcement of <see cref="ContainerOwnershipGuard"/> before any verb
/// is issued (trust-boundaries.md §4), and creation is built from the closed <see cref="PzContainerFactory"/>
/// template (ADR 0008 §5.3). F13 provides these capabilities and proves them with tests; the operator-facing
/// commands that call the mutating ones arrive with F14 (registration) and F15 (lifecycle Operations).
/// </summary>
public interface IContainerRuntime
{
    /// <summary>Probes Docker connectivity: daemon reachability and the negotiated API version.</summary>
    Task<DockerHealth> ProbeHealthAsync(CancellationToken cancellationToken);

    /// <summary>Lists the canonical containers this Agent owns; foreign and non-canonical ones are excluded.</summary>
    Task<IReadOnlyList<ManagedContainer>> ListManagedAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Inspects every canonical container this Agent owns and returns its health-relevant facts (F16): state,
    /// HEALTHCHECK verdict, exit/OOM, and published ports. A container that vanishes between the list and its
    /// inspect is skipped rather than failing the whole sweep. Foreign and non-canonical containers are excluded
    /// exactly as <see cref="ListManagedAsync"/>.
    /// </summary>
    Task<IReadOnlyList<ObservedContainer>> InspectManagedAsync(CancellationToken cancellationToken);

    /// <summary>Allocates the next free two-port-stride pair given the strides already occupied on the host.</summary>
    Task<PortAllocation> AllocateNextPortsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Creates a canonical container from a closed spec and returns its Docker id. Throws
    /// <see cref="ContainerCreateException"/> with an actionable failure — notably
    /// <see cref="ContainerCreateFailure.ImageNotProvisioned"/> when the pinned image is not pre-provisioned.
    /// </summary>
    Task<string> CreateAsync(PzContainerSpec spec, CancellationToken cancellationToken);

    /// <summary>Starts a container the Agent owns. Refuses a foreign container with <see cref="ForeignContainerException"/>.</summary>
    Task StartAsync(string containerId, CancellationToken cancellationToken);

    /// <summary>Stops a container the Agent owns. Refuses a foreign container with <see cref="ForeignContainerException"/>.</summary>
    Task StopAsync(string containerId, CancellationToken cancellationToken);

    /// <summary>Restarts a container the Agent owns. Refuses a foreign container with <see cref="ForeignContainerException"/>.</summary>
    Task RestartAsync(string containerId, CancellationToken cancellationToken);

    /// <summary>Starts the canonical container this Agent owns for <paramref name="serverId"/> (F15). Throws
    /// <see cref="ContainerNotFoundException"/> when no owned container carries that ServerId.</summary>
    Task StartAsync(ServerId serverId, CancellationToken cancellationToken);

    /// <summary>Stops the canonical container this Agent owns for <paramref name="serverId"/> safely (F15): a
    /// Docker stop whose timeout lets the image's entrypoint run the console <c>save</c>→<c>quit</c> over the
    /// FIFO. Throws <see cref="ContainerNotFoundException"/> when no owned container carries that ServerId.</summary>
    Task StopAsync(ServerId serverId, CancellationToken cancellationToken);

    /// <summary>Restarts the canonical container this Agent owns for <paramref name="serverId"/> safely (F15),
    /// carrying the same safe stop timeout as <see cref="StopAsync(ServerId, CancellationToken)"/>. Throws
    /// <see cref="ContainerNotFoundException"/> when no owned container carries that ServerId.</summary>
    Task RestartAsync(ServerId serverId, CancellationToken cancellationToken);
}
