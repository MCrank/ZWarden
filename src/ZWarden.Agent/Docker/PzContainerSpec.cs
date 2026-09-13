using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Docker;

/// <summary>
/// The closed set of inputs for creating a canonical ZWarden.PZServer container. This type deliberately
/// exposes <b>only</b> the safe, domain-shaped knobs — the Server it is for, the pinned image, the ZWarden
/// network, the host data directory, the allocated ports and the memory limit. It has <b>no</b> field for
/// <c>Privileged</c>, capabilities, devices, host namespaces or the container user, because those are host
/// escapes no socket proxy can block (ADR 0008, research §5.3): the create body is an Agent-side correctness
/// requirement, so the unsafe options are not merely defaulted — they are <i>unexpressible</i>. The Agent id
/// stamped onto the container is not here either; it comes from the Agent's own identity, never the caller.
/// </summary>
/// <param name="ServerId">The Server this container hosts; stamped as <c>io.zwarden.server-id</c>.</param>
/// <param name="ContainerName">The Docker container name.</param>
/// <param name="ImageReference">
/// The pinned canonical PZ image reference (a <c>repo@sha256:…</c> digest in production; ADR 0008 D5). Supplied
/// from configuration, never from caller input; a floating <c>latest</c> tag is rejected.
/// </param>
/// <param name="NetworkName">The named ZWarden network to attach (PRD 26); never <c>host</c>/<c>none</c>.</param>
/// <param name="DataMountSource">The absolute host path bind-mounted read-write at <c>/pz/data</c> (PRD 23).</param>
/// <param name="ServerMountSource">
/// The absolute host path bind-mounted read-write at <c>/pz/server</c> (F17): the SteamCMD install and its Steam
/// app manifest, persisted so an update is incremental and the installed build id survives a container recreate.
/// A host sibling of the data directory, so it is not counted by the data-volume disk meter (F16).
/// </param>
/// <param name="Ports">The two-port-stride host allocation (PRD 28).</param>
/// <param name="MemoryLimitBytes">The container memory limit in bytes (PRD 24 resource limits); must be positive.</param>
public sealed record PzContainerSpec(
    ServerId ServerId,
    string ContainerName,
    string ImageReference,
    string NetworkName,
    string DataMountSource,
    string ServerMountSource,
    PortAllocation Ports,
    long MemoryLimitBytes);
