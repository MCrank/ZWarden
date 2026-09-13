using ZWarden.Domain.Ids;

namespace ZWarden.Application.Servers;

/// <summary>
/// The latest runtime-metrics sample ZWarden.Web holds for a Server (F16), mapped from the wire report by
/// ZWarden.Web (the Application layer references no Contracts). It carries the reporting <see cref="AgentId"/> so
/// the cache can refuse a sample from an Agent that does not own the Server (a cheap in-memory ownership guard
/// on read, trust-boundaries.md §8). Observed, transient, non-secret.
/// </summary>
/// <param name="AgentId">The Agent that reported the sample.</param>
/// <param name="ServerId">The Server the sample is for.</param>
/// <param name="CpuPercent">CPU utilisation as a percentage of the container's allotted CPU.</param>
/// <param name="MemoryUsedBytes">Resident memory in use, in bytes.</param>
/// <param name="MemoryLimitBytes">The container's memory limit, in bytes.</param>
/// <param name="DiskUsedBytes">Bytes used under the Server's data directory, or <c>null</c> when not measured.</param>
/// <param name="DiskCapacityBytes">The data volume's capacity in bytes, or <c>null</c> when not measured.</param>
/// <param name="PlayerCount">Connected players, or <c>null</c> (always null in v1.0 — RCON is F18).</param>
/// <param name="SampledAt">When the Agent took the sample (UTC).</param>
public sealed record ServerMetrics(
    AgentId AgentId,
    ServerId ServerId,
    double CpuPercent,
    long MemoryUsedBytes,
    long MemoryLimitBytes,
    long? DiskUsedBytes,
    long? DiskCapacityBytes,
    int? PlayerCount,
    DateTimeOffset SampledAt);
