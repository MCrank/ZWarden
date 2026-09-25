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
/// <param name="PlayerCount">Connected players from the Agent's last RCON sample (#257), or <c>null</c> when there is
/// none (not running, RCON unavailable, or not sampled yet).</param>
/// <param name="SampledAt">When the Agent took the sample (UTC).</param>
/// <param name="PlayerCountSampledAt">When <paramref name="PlayerCount"/> was read (UTC) — it is sampled on a slower
/// cadence than the rest, so the UI can say "as of N min ago" — or <c>null</c> with no count.</param>
/// <param name="StartedAt">The container's start time (UTC) while running — uptime is <c>now − StartedAt</c> — or
/// <c>null</c>. Cache-only: the next report re-reads it from Docker after a Web restart (#257).</param>
/// <param name="InstalledBuildId">The Steam build id from the install manifest (observed, untrusted), or <c>null</c>.</param>
/// <param name="GameVersion">The game version from the boot log (#262; observed, untrusted), or <c>null</c>.</param>
public sealed record ServerMetrics(
    AgentId AgentId,
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
