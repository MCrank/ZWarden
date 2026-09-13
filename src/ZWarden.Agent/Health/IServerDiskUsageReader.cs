namespace ZWarden.Agent.Health;

/// <summary>A Server's data-directory disk usage (F16): bytes used under it and the capacity of the volume it
/// sits on. Either may be <c>null</c> when it could not be measured (the directory is absent, or the read
/// failed) — the meter then shows "unknown" rather than a wrong figure.</summary>
/// <param name="UsedBytes">Bytes used under the data directory, or <c>null</c>.</param>
/// <param name="CapacityBytes">The underlying volume's total capacity in bytes, or <c>null</c>.</param>
public readonly record struct DiskUsage(long? UsedBytes, long? CapacityBytes);

/// <summary>
/// Reads a Server's disk usage from the host bind-mount directly (F16) — no Docker, because the Agent owns
/// <c>DataMountRoot</c>. Best-effort: a missing directory or an I/O error yields <c>null</c> figures rather than
/// throwing, so one unreadable Server never fails a whole metrics sweep.
/// </summary>
public interface IServerDiskUsageReader
{
    /// <summary>Measures usage under <paramref name="dataDirectory"/> and the capacity of its volume.</summary>
    DiskUsage Read(string dataDirectory);
}
