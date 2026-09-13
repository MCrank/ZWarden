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
/// <c>docker stats</c>; disk from the host bind-mount; <see cref="PlayerCount"/> is <c>null</c> in v1.0 (it needs
/// RCON — F18, which depends on F16). Byte counts are non-negative; percentages are 0–100 per logical view but
/// not clamped here.
/// </summary>
/// <param name="ServerId">The Server this sample is for (matched to a persisted Server on ingest).</param>
/// <param name="CpuPercent">CPU utilisation as a percentage of the container's allotted CPU.</param>
/// <param name="MemoryUsedBytes">Resident memory in use, in bytes.</param>
/// <param name="MemoryLimitBytes">The container's memory limit, in bytes (the meter's denominator).</param>
/// <param name="DiskUsedBytes">Bytes used under the Server's data directory, or <c>null</c> when not measured.</param>
/// <param name="DiskCapacityBytes">The data volume's capacity in bytes, or <c>null</c> when not measured.</param>
/// <param name="PlayerCount">Connected players, or <c>null</c> (always null in v1.0 — RCON is F18).</param>
/// <param name="SampledAt">When the Agent took the sample (UTC).</param>
public sealed record ServerMetricsSample(
    ServerId ServerId,
    double CpuPercent,
    long MemoryUsedBytes,
    long MemoryLimitBytes,
    long? DiskUsedBytes,
    long? DiskCapacityBytes,
    int? PlayerCount,
    DateTimeOffset SampledAt);
