namespace ZWarden.Agent.Docker;

/// <summary>
/// Materialises a Server's host-side bind-mount source directories before its canonical container is created
/// (#184). The Docker <c>Mounts</c> API (unlike a <c>-v</c> bind) does NOT auto-create a bind source, and the
/// Agent runs in its own container, so both sources — the world-data mount (<c>/pz/data</c>) and the SteamCMD
/// install mount (<c>/pz/server</c>, F17) — must exist on the shared host path the daemon resolves them against.
/// Without this the daemon refuses the create with <c>HTTP 400 "bind source path does not exist"</c>. Kept behind
/// a seam so the provisioning flow stays unit-testable without touching the real filesystem.
/// </summary>
public interface IServerHostDirectories
{
    /// <summary>Ensures the data and server-install bind-mount source directories for <paramref name="spec"/>
    /// exist. Idempotent. Throws <see cref="IOException"/>/<see cref="UnauthorizedAccessException"/> if the
    /// host paths cannot be prepared — the provisioning flow reports that as an actionable failure.</summary>
    void EnsureCreated(PzContainerSpec spec);
}

/// <inheritdoc />
public sealed class ServerHostDirectories : IServerHostDirectories
{
    /// <inheritdoc />
    public void EnsureCreated(PzContainerSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        // Both bind sources must pre-exist for the daemon's Mounts API; creating the data root also gives the
        // RCON seed (and later F17/F24/F20/F21 host-side writes) a directory to land in.
        Directory.CreateDirectory(spec.DataMountSource);
        Directory.CreateDirectory(spec.ServerMountSource);
    }
}
