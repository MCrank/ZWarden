namespace ZWarden.Infrastructure.Agents;

/// <summary>
/// Tuning for the Agent connection monitor (F10). A connection is treated as stale once nothing has been
/// heard from it for <see cref="StaleAfter"/> — a multiple of the Agent's heartbeat interval, so a single
/// missed heartbeat is not enough. The monitor is the safety net for a keepalive that has not yet fired and
/// for a Web crash that left a persisted <c>Connected</c> row with nobody to clear it.
/// </summary>
public sealed class AgentConnectionMonitorOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Agent:ConnectionMonitor";

    /// <summary>How long without an observation before a connection is reconciled to disconnected. Default 90s
    /// (three 30s heartbeats).</summary>
    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>How often the monitor sweeps for stale connections. Default 30s.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(30);
}
