using Docker.DotNet.Models;
using ZWarden.Agent.Identity;

namespace ZWarden.Agent.Docker;

/// <summary>
/// Builds the <see cref="CreateContainerParameters"/> for a canonical ZWarden.PZServer container from a closed
/// <see cref="PzContainerSpec"/> template. This is the correctness boundary ADR 0008 identifies: no socket
/// proxy can constrain the create body, so the twelve invariants of research §5.3 are enforced <b>here</b>,
/// structurally. The unsafe fields (<c>Privileged</c>, host namespaces, devices, added capabilities) are
/// never derived from input — they are fixed to their safe values or left unset — and the owning Agent id is
/// read from this Agent's own identity, never from the caller. A spec that names a dangerous network or a
/// floating image tag is rejected before any container is created.
/// </summary>
public sealed class PzContainerFactory
{
    private const string DataMountTarget = "/pz/data";
    private const string ServerMountTarget = "/pz/server";
    private const string RuntimeTmpfsTarget = "/pz/runtime";
    // #184: SteamCMD needs a writable temp dir (mktemp + its breakpad /tmp/dumps) and a writable $HOME
    // (~/.steam, ~/.local). Under Invariant 8's read-only rootfs those paths would be read-only, so /tmp is an
    // ephemeral tmpfs and HOME/TMPDIR are redirected onto the writable /pz/runtime tmpfs (below).
    private const string TmpTmpfsTarget = "/tmp";
    // exec so SteamCMD can run from the copied-in client; mode 1777 so the non-root user can write it.
    private const string RuntimeTmpfsOptions = "exec,mode=1777";
    private const string GamePortKey = "16261/udp";
    private const string DirectPortKey = "16262/udp";
    private const string BindAddress = "0.0.0.0";
    private const string NonRootUser = "10000:10000";

    private readonly IAgentIdentity _identity;

    /// <summary>Creates the factory bound to this Agent's identity (the id it stamps onto containers).</summary>
    public PzContainerFactory(IAgentIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        _identity = identity;
    }

    /// <summary>
    /// Builds the create parameters for <paramref name="spec"/>, enforcing every §5.3 invariant. Throws
    /// <see cref="ArgumentException"/> if the spec names a dangerous network, a floating image tag, a
    /// non-absolute mount source, or a non-positive memory limit.
    /// </summary>
    public CreateContainerParameters Build(PzContainerSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        Validate(spec);

        return new CreateContainerParameters
        {
            Name = spec.ContainerName,
            // Invariant 9: the image is exactly the configured pinned reference — no default, no override.
            Image = spec.ImageReference,
            // Invariant 11: a non-root user.
            User = NonRootUser,
            // Invariant 10: the full canonical label set, including this Agent's own id.
            Labels = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [CanonicalLabels.Managed] = CanonicalLabels.ManagedValue,
                [CanonicalLabels.Runtime] = CanonicalLabels.RuntimeValue,
                [CanonicalLabels.SchemaVersion] = CanonicalLabels.SchemaVersionValue,
                [CanonicalLabels.ServerId] = spec.ServerId.ToString(),
                [CanonicalLabels.AgentId] = _identity.AgentId.ToString(),
            },
            // Invariant 12: only the two intended UDP ports are exposed; RCON is never here.
            ExposedPorts = new Dictionary<string, EmptyStruct>(StringComparer.Ordinal)
            {
                [GamePortKey] = default,
                [DirectPortKey] = default,
            },
            // #184: point SteamCMD's writable needs at the /pz/runtime tmpfs so it runs under the read-only
            // rootfs (Invariant 8). Overrides the image's defaults (/home/pzserver, /tmp) only for the managed
            // container — the standalone image is unchanged.
            // #198: own the JVM heap here alongside the memory limit so the two can never drift. Overrides the
            // image's standalone ZW_PZ_XMS/XMX defaults for the managed container only; the memory limit below is
            // derived from this heap plus fixed headroom, so a fresh world's off-heap boot spike does not OOM.
            Env =
            [
                $"HOME={RuntimeTmpfsTarget}",
                $"TMPDIR={RuntimeTmpfsTarget}",
                $"ZW_PZ_XMS={FormatJvmHeap(spec.HeapSizeBytes)}",
                $"ZW_PZ_XMX={FormatJvmHeap(spec.HeapSizeBytes)}",
            ],
            HostConfig = new HostConfig
            {
                // Invariant 1: never privileged.
                Privileged = false,
                // Invariant 2: drop all capabilities; add none.
                CapDrop = ["ALL"],
                CapAdd = [],
                // Invariant 3: no new privileges.
                SecurityOpt = ["no-new-privileges:true"],
                // Invariant 4: a named ZWarden network (validated not host/none/container:).
                NetworkMode = spec.NetworkName,
                // Invariant 6: no devices of any kind.
                Devices = [],
                // Invariant 7: the only writable storage is the three intended mounts — the /pz/data world
                // bind, the /pz/server install bind (F17), and the /pz/runtime tmpfs (below); no legacy binds.
                Binds = [],
                Mounts =
                [
                    new Mount
                    {
                        Type = "bind",
                        Source = spec.DataMountSource,
                        Target = DataMountTarget,
                        ReadOnly = false,
                    },
                    new Mount
                    {
                        Type = "bind",
                        Source = spec.ServerMountSource,
                        Target = ServerMountTarget,
                        ReadOnly = false,
                    },
                ],
                // Invariant 7 (cont.): /pz/runtime is an ephemeral tmpfs, not a persistent bind — SteamCMD's
                // self-updating client and the stdin FIFO live here and need writable, exec-capable storage
                // (F17), but nothing under it must survive a recreate (Workshop content is F21).
                Tmpfs = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [RuntimeTmpfsTarget] = RuntimeTmpfsOptions,
                    // #184: a small writable /tmp for SteamCMD (breakpad /tmp/dumps and any tmp it execs).
                    [TmpTmpfsTarget] = RuntimeTmpfsOptions,
                },
                // Invariant 8: read-only root filesystem; only the two binds and the runtime tmpfs are writable.
                ReadonlyRootfs = true,
                // Invariant 11: a resource limit.
                Memory = spec.MemoryLimitBytes,
                // Invariant 12: the two-port stride, on the intended interface. RCON (27015/tcp) is unmapped.
                PortBindings = new Dictionary<string, IList<PortBinding>>(StringComparer.Ordinal)
                {
                    [GamePortKey] = [new PortBinding { HostIP = BindAddress, HostPort = spec.Ports.GamePort.ToString(System.Globalization.CultureInfo.InvariantCulture) }],
                    [DirectPortKey] = [new PortBinding { HostIP = BindAddress, HostPort = spec.Ports.DirectPort.ToString(System.Globalization.CultureInfo.InvariantCulture) }],
                },
                // Invariant 5: host PID/IPC/userns/UTS/cgroup namespaces are left unset — never "host".
            },
        };
    }

    private static void Validate(PzContainerSpec spec)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.ContainerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.ImageReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.NetworkName);
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.DataMountSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.ServerMountSource);

        // Invariant 9: a floating tag is never acceptable — production pins a digest.
        if (spec.ImageReference.Equals("latest", StringComparison.Ordinal)
            || spec.ImageReference.EndsWith(":latest", StringComparison.Ordinal))
        {
            throw new ArgumentException("The canonical PZ image reference must be pinned, never a floating 'latest' tag.", nameof(spec));
        }

        // Invariant 4: the network must be a named ZWarden network, never a host-escaping mode.
        if (spec.NetworkName is "host" or "none" or "default"
            || spec.NetworkName.StartsWith("container:", StringComparison.Ordinal))
        {
            throw new ArgumentException($"'{spec.NetworkName}' is not a permitted container network.", nameof(spec));
        }

        // Invariant 7: the bind mount sources must be absolute (Linux) host paths.
        if (!spec.DataMountSource.StartsWith('/'))
        {
            throw new ArgumentException("The data mount source must be an absolute host path.", nameof(spec));
        }

        if (!spec.ServerMountSource.StartsWith('/'))
        {
            throw new ArgumentException("The server mount source must be an absolute host path.", nameof(spec));
        }

        if (spec.HeapSizeBytes <= 0)
        {
            throw new ArgumentException("The JVM heap size must be positive.", nameof(spec));
        }

        // Invariant 11 (cont.): the limit must leave headroom over the heap, or the container OOM-kills on boot
        // (#198) — ZGC/native/metaspace and PZ's off-heap world load run well above the Java heap.
        if (spec.MemoryLimitBytes <= spec.HeapSizeBytes)
        {
            throw new ArgumentException("The container memory limit must exceed the JVM heap size.", nameof(spec));
        }
    }

    // The image tunes -Xms/-Xmx from ZW_PZ_XMS/XMX (pz-lib.sh pz_tune_jvm), which accept a JVM size suffix. Emit
    // whole mebibytes so the value is exact and always a legal heap string (e.g. 4 GiB -> "4096m").
    private static string FormatJvmHeap(long bytes) =>
        $"{bytes / (1024 * 1024)}m";
}
