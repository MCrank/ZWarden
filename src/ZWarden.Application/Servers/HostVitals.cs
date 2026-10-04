namespace ZWarden.Application.Servers;

/// <summary>
/// A host's live vitals for the Hosts card meters (#170), carried on the latest <see cref="HostCapacity"/>. Each figure
/// is <c>null</c> when the Agent couldn't read it (or predates #170). <b>Observed, not trusted</b> (trust-boundaries.md
/// §3): display only.
/// </summary>
/// <param name="CpuPercent">Host-wide CPU busy %, 0–100.</param>
/// <param name="MemoryUsedBytes">Host RAM in use; the total is <see cref="HostCapacity.TotalBytes"/>.</param>
/// <param name="DiskFreeBytes">Free space on the volume holding the PZ data root.</param>
/// <param name="DiskTotalBytes">The total size of that volume.</param>
public sealed record HostVitals(double? CpuPercent, long? MemoryUsedBytes, long? DiskFreeBytes, long? DiskTotalBytes)
{
    /// <summary>
    /// The vitals worth keeping from an Agent's report: a CPU share outside 0–100, memory in use that is negative or above
    /// <paramref name="memoryTotalBytes"/>, or a disk pair whose free space is negative or above a positive total is
    /// dropped (that figure, or the disk pair). <c>null</c> when nothing is left.
    /// </summary>
    public static HostVitals? Observed(
        double? cpuPercent, long? memoryUsedBytes, long memoryTotalBytes, long? diskFreeBytes, long? diskTotalBytes)
    {
        double? cpu = cpuPercent is >= 0 and <= 100 ? cpuPercent : null;
        long? memory = memoryUsedBytes is { } used && used >= 0 && used <= memoryTotalBytes ? used : null;
        bool disk = diskFreeBytes is { } free && diskTotalBytes is { } total && free >= 0 && total > 0 && free <= total;

        return cpu is null && memory is null && !disk
            ? null
            : new HostVitals(cpu, memory, disk ? diskFreeBytes : null, disk ? diskTotalBytes : null);
    }
}
