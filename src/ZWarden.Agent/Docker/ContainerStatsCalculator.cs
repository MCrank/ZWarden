namespace ZWarden.Agent.Docker;

/// <summary>
/// Turns a <see cref="ContainerStatsSnapshot"/> into a CPU percentage and byte figures (F16) — the same maths
/// the <c>docker stats</c> CLI does, as a <b>pure</b> function so it is fully unit-tested. CPU% is the container
/// CPU delta over the host CPU delta, scaled by the online CPU count; memory is usage minus reclaimable cache.
/// Every delta is guarded so a counter reset or a first-ever read (no previous counters) yields 0, never a
/// negative or a divide-by-zero.
/// </summary>
public static class ContainerStatsCalculator
{
    /// <summary>The CPU percentage (0–100·nCPU) from the snapshot's deltas; 0 when either delta is non-positive.</summary>
    public static double CpuPercent(ContainerStatsSnapshot s)
    {
        double cpuDelta = Delta(s.CpuTotalUsage, s.PreCpuTotalUsage);
        double systemDelta = Delta(s.SystemCpuUsage, s.PreSystemCpuUsage);
        if (cpuDelta <= 0 || systemDelta <= 0)
        {
            return 0;
        }

        double cpus = s.OnlineCpus > 0 ? s.OnlineCpus : 1;
        return cpuDelta / systemDelta * cpus * 100;
    }

    /// <summary>Resident memory in bytes: usage minus reclaimable cache, clamped at 0.</summary>
    public static long MemoryUsedBytes(ContainerStatsSnapshot s)
    {
        ulong used = s.MemoryUsage > s.MemoryCache ? s.MemoryUsage - s.MemoryCache : 0;
        return ToSignedBytes(used);
    }

    /// <summary>The container's memory limit in bytes.</summary>
    public static long MemoryLimitBytes(ContainerStatsSnapshot s) => ToSignedBytes(s.MemoryLimit);

    private static double Delta(ulong current, ulong previous) =>
        current > previous ? current - previous : 0;

    private static long ToSignedBytes(ulong value) =>
        value > long.MaxValue ? long.MaxValue : (long)value;
}
