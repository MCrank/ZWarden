namespace ZWarden.Agent.Docker;

/// <summary>
/// The facts a Recreate needs about the canonical container this Agent owns for a Server (#229), read from an
/// authoritative inspect: whether it is running (so the prior run state is preserved), the host pair it was created
/// with (kept when no new port is requested, and the rollback target), and its bind mounts (Recreate refuses a
/// container whose data binds are not the ServerId-derived ones).
/// </summary>
/// <param name="DockerId">The Docker container id.</param>
/// <param name="State">The container's state string (e.g. <c>running</c>, <c>exited</c>).</param>
/// <param name="Ports">The host pair bound to the container's 16261/16262 udp, or <c>null</c> when it has none.</param>
/// <param name="BindMounts">Bind mounts as container destination → host source.</param>
public sealed record ServerContainer(
    string DockerId,
    string State,
    PortAllocation? Ports,
    IReadOnlyDictionary<string, string> BindMounts)
{
    /// <summary>Whether the container is up (running, restarting or paused) — anything but a stopped state.</summary>
    public bool IsRunning => State is "running" or "restarting" or "paused";
}
