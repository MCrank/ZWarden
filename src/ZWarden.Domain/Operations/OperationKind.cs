namespace ZWarden.Domain.Operations;

/// <summary>
/// What an <see cref="Operation"/> does. Stored by name (never its ordinal), so entries may be reordered
/// but not renumbered. F11 ships only <see cref="DiagnosticsPing"/>; the real mutating kinds
/// (<c>RestartServer</c>, <c>ApplyConfig</c>, <c>Backup</c>, …) land with their owning features (F13/F15/…),
/// each declaring its own mutating-ness at enqueue. Whether an Operation takes the per-server lock is
/// <see cref="Operation.IsMutating"/>, set at enqueue — not derived from this enum — so the lock is
/// testable before any mutating kind exists (ADR 0022).
/// </summary>
public enum OperationKind
{
    /// <summary>A non-mutating round-trip probe of a Server's Agent: dispatch a command and observe the
    /// reply. The first diagnostic tool for "is this Agent actually round-tripping commands right now?".
    /// Being non-mutating, it never claims the per-server lock.</summary>
    DiagnosticsPing = 0,

    /// <summary>A non-mutating, host-level probe of the Agent's Docker connectivity (F13): daemon reachability
    /// and the negotiated API version. It acts on the host Agent, not a Server, so it never claims a per-server
    /// lock (ADR 0022: host-level Operations never contend).</summary>
    DiagnosticsDockerHealth = 1,

    /// <summary>Provision the canonical ZWarden.PZServer container for a registered Server (F14). A
    /// <b>mutating, server-scoped</b> Operation, so it claims the per-server lock (ADR 0022). The Agent builds
    /// the container from F13's closed create-template and allocates the port stride itself, then reports the
    /// allocated ports and container id on completion.</summary>
    ProvisionServer = 2,

    /// <summary>Start a registered Server's canonical container (F15). A <b>mutating, server-scoped</b>
    /// Operation, so it claims the per-server lock (ADR 0022). The Agent resolves the container it owns for the
    /// Server and issues the Docker start verb.</summary>
    StartServer = 3,

    /// <summary>Stop a registered Server's canonical container safely (F15). A <b>mutating, server-scoped</b>
    /// Operation (per-server lock, ADR 0022). The Agent issues a Docker stop with a timeout above the image's
    /// save grace, so the entrypoint's SIGTERM handler runs the console <c>save</c>→<c>quit</c> over the stdin
    /// FIFO — never a bare SIGTERM to the JVM.</summary>
    StopServer = 4,

    /// <summary>Restart a registered Server's canonical container safely (F15). A <b>mutating, server-scoped</b>
    /// Operation (per-server lock, ADR 0022). The Agent issues a Docker restart carrying the same safe stop
    /// timeout as <see cref="StopServer"/>.</summary>
    RestartServer = 5,

    /// <summary>Update (install/validate) a Server's Project Zomboid install via anonymous SteamCMD (F17). A
    /// <b>mutating, server-scoped</b> Operation, so it holds the per-server lock (ADR 0022) for the whole
    /// SteamCMD run — a multi-minute operation kept alive under the reaper by its <c>OperationProgress</c>
    /// reports. Install/update/validate are one SteamCMD verb, so a repair is this same kind run again. The
    /// Agent drives it through the container entrypoint (no <c>exec</c>, ADR 0008) and decides the outcome by
    /// parsing stdout (ADR 0009).</summary>
    UpdateServer = 6,
}
