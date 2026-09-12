namespace ZWarden.Infrastructure.Operations;

/// <summary>
/// Tunables for the operations engine (F11; ADR 0022). Defaults are deliberate and conservative for a
/// self-hosted, human-paced workload (operations per minute, not per second).
/// </summary>
public sealed class OperationEngineOptions
{
    /// <summary>How long a dispatched Operation's lease runs before the reaper may fail it. Extended by each
    /// Agent progress report, so this bounds silence, not total duration. A dead/unresponsive Agent frees the
    /// per-server lock after at most this long without a report.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How often the reaper sweeps for expired leases.</summary>
    public TimeSpan ReaperInterval { get; set; } = TimeSpan.FromSeconds(30);
}
