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

    /// <summary>Reads the host's memory budget (#230): the daemon's total RAM and the summed memory limits of every
    /// container this Agent owns, stopped ones included. A container that vanishes mid-read is skipped.</summary>
    Task<HostMemory> ReadHostMemoryAsync(CancellationToken cancellationToken);

    /// <summary>Allocates the lowest two-port-stride pair whose ports no container on the daemon publishes or holds
    /// (running or stopped, owned or not — #229).</summary>
    Task<PortAllocation> AllocateNextPortsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Validates an operator-requested host game port for <paramref name="forServer"/> (#229) and returns its pair
    /// (<c>p</c>, <c>p + 1</c>). Throws <see cref="PortUnavailableException"/> when the port is out of range or either
    /// port is already held by another container on the daemon. The Server's own container is not a clash — a
    /// Recreate is about to remove it. This is a pre-flight: Docker's start remains the authority for non-Docker
    /// host processes.
    /// </summary>
    Task<PortAllocation> ClaimRequestedPortsAsync(int gamePort, ServerId forServer, CancellationToken cancellationToken);

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

    /// <summary>Inspects the canonical container this Agent owns for <paramref name="serverId"/> and returns the facts
    /// a Recreate needs (#229), or <c>null</c> when no owned container carries that ServerId.</summary>
    Task<ServerContainer?> InspectServerAsync(ServerId serverId, CancellationToken cancellationToken);

    /// <summary>Removes the canonical container this Agent owns for <paramref name="serverId"/> (#229, ADR 0045):
    /// ownership is re-asserted on inspect, the container must be stopped and named by its ServerId, and the delete is
    /// never forced and never removes volumes. The ServerId-derived bind mounts — world, config, installed build — are
    /// host directories and survive. Throws <see cref="ContainerNotFoundException"/> when no owned container matches,
    /// <see cref="ForeignContainerException"/> when inspect shows it is not ours, and
    /// <see cref="InvalidOperationException"/> when it is running or misnamed.</summary>
    Task RemoveAsync(ServerId serverId, CancellationToken cancellationToken);

    /// <summary>Reads the logs of the canonical container this Agent owns for <paramref name="serverId"/> (F17):
    /// stdout+stderr, non-following, since an optional time. This is how the Agent observes a SteamCMD update it
    /// cannot <c>exec</c> (ADR 0008). Throws <see cref="ContainerNotFoundException"/> when no owned container
    /// carries that ServerId.</summary>
    Task<string> ReadServerLogsAsync(ServerId serverId, DateTimeOffset? since, CancellationToken cancellationToken);

    /// <summary>Follows the logs of the canonical container this Agent owns for <paramref name="serverId"/> as a
    /// live stream (F27): stdout+stderr, backfilling the last <paramref name="tailLines"/> lines then streaming
    /// until <paramref name="cancellationToken"/> is cancelled. Each raw, <b>unsanitized</b> line is delivered to
    /// <paramref name="onFrame"/>; the caller sanitizes (PRD 38). Discovery already scopes to owned containers, so
    /// this throws <see cref="ContainerNotFoundException"/> when no owned container carries that ServerId — a
    /// foreign or absent Server is nothing to follow.</summary>
    Task FollowServerLogsAsync(
        ServerId serverId,
        int tailLines,
        Func<ContainerLogFrame, CancellationToken, ValueTask> onFrame,
        CancellationToken cancellationToken);

    /// <summary>Resolves the IP address of the canonical container this Agent owns for <paramref name="serverId"/>
    /// on the named network (F18): inspects the container and reads
    /// <c>NetworkSettings.Networks[networkName].IPAddress</c>. Returns <c>null</c> when no owned container carries
    /// that ServerId, or it has no address on that network (e.g. it is not running). This is how the Agent reaches
    /// the private, never-host-published RCON port.</summary>
    Task<string?> ResolveNetworkAddressAsync(ServerId serverId, string networkName, CancellationToken cancellationToken);
}
