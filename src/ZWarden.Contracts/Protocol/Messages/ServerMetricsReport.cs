using ZWarden.Domain.Ids;

namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// The Agent's periodic runtime-metrics report (F16): the latest resource sample for each Server it manages.
/// Metrics are high-churn and transient — ZWarden.Web keeps only the <b>latest</b> sample per Server in memory
/// and pushes it to the live UI; it is never a per-sample audit or a persisted time-series. Observed data
/// (trust-boundaries.md §3); a sample for a Server the reporting Agent does not own is ignored on ingest.
/// </summary>
/// <param name="Samples">One latest sample per managed Server.</param>
[ProtocolMessage("server.metrics-report")]
public sealed record ServerMetricsReport(IReadOnlyList<ServerMetricsSample> Samples) : AgentEvent;

/// <summary>
/// One Server's resource sample within a <see cref="ServerMetricsReport"/> (F16). CPU and memory come from
/// <c>docker stats</c>; disk from the host bind-mount. Byte counts are non-negative; percentages are 0–100 per
/// logical view but not clamped here. The fleet facts added by #257 (player count, container start time, installed
/// build) and #262 (game version) are trailing, nullable members, so the change is additive under ADR 0020 (an Agent that
/// omits them deserializes as <c>null</c>; no protocol bump).
/// </summary>
/// <param name="ServerId">The Server this sample is for (matched to a persisted Server on ingest).</param>
/// <param name="CpuPercent">CPU utilisation as a percentage of the container's allotted CPU.</param>
/// <param name="MemoryUsedBytes">Resident memory in use, in bytes.</param>
/// <param name="MemoryLimitBytes">The container's memory limit, in bytes (the meter's denominator).</param>
/// <param name="DiskUsedBytes">Bytes used under the Server's data directory, or <c>null</c> when not measured.</param>
/// <param name="DiskCapacityBytes">The data volume's capacity in bytes, or <c>null</c> when not measured.</param>
/// <param name="PlayerCount">Connected players from the Agent's last RCON <c>players</c> sample (#257), or
/// <c>null</c> when the Server is not running, RCON is unavailable, or no sample has been taken yet. It is sampled
/// on its own slower cadence, so it can be older than <paramref name="SampledAt"/>.</param>
/// <param name="SampledAt">When the Agent took the sample (UTC).</param>
/// <param name="PlayerCountSampledAt">When <paramref name="PlayerCount"/> was read over RCON (UTC), or <c>null</c>
/// when there is no count.</param>
/// <param name="StartedAt">The container's Docker <c>State.StartedAt</c> (UTC) while it is running — the source of
/// the fleet uptime (#257) — or <c>null</c> when it is not running or the inspect failed.</param>
/// <param name="InstalledBuildId">The Steam build id read from the install volume's app manifest (#257; observed,
/// untrusted), or <c>null</c> when the manifest is absent or unreadable.</param>
/// <param name="GameVersion">The Project Zomboid game version (e.g. <c>42.20.4</c>) the server printed at boot (#262;
/// observed, untrusted), or <c>null</c> when it has not been read yet.</param>
public sealed record ServerMetricsSample(
    ServerId ServerId,
    double CpuPercent,
    long MemoryUsedBytes,
    long MemoryLimitBytes,
    long? DiskUsedBytes,
    long? DiskCapacityBytes,
    int? PlayerCount,
    DateTimeOffset SampledAt,
    DateTimeOffset? PlayerCountSampledAt = null,
    DateTimeOffset? StartedAt = null,
    string? InstalledBuildId = null,
    string? GameVersion = null);
