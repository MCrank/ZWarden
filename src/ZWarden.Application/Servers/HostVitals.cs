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
/// <param name="CpuCores">The host's logical CPUs.</param>
/// <param name="LoadAverage1">The 1-minute load average; the three are all known or all <c>null</c>.</param>
/// <param name="LoadAverage5">The 5-minute load average.</param>
/// <param name="LoadAverage15">The 15-minute load average.</param>
public sealed record HostVitals(
    double? CpuPercent,
    long? MemoryUsedBytes,
    long? DiskFreeBytes,
    long? DiskTotalBytes,
    int? CpuCores = null,
    double? LoadAverage1 = null,
    double? LoadAverage5 = null,
    double? LoadAverage15 = null)
{
    // Generous ceilings: anything beyond them is not a real host, so it is dropped rather than shown.
    private const int MaxCores = 4096;
    private const double MaxLoad = 100_000;

    /// <summary>
    /// The vitals worth keeping from an Agent's report: a CPU share outside 0–100, memory in use that is negative or above
    /// <paramref name="memoryTotalBytes"/>, a disk pair whose free space is negative or above a positive total, a core
    /// count outside 1–4096, or a load average that is negative or not a number is dropped (that figure, the disk pair,
    /// or all three loads). <c>null</c> when nothing is left.
    /// </summary>
    public static HostVitals? Observed(
        double? cpuPercent,
        long? memoryUsedBytes,
        long memoryTotalBytes,
        long? diskFreeBytes,
        long? diskTotalBytes,
        int? cpuCores = null,
        double? loadAverage1 = null,
        double? loadAverage5 = null,
        double? loadAverage15 = null)
    {
        double? cpu = cpuPercent is >= 0 and <= 100 ? cpuPercent : null;
        long? memory = memoryUsedBytes is { } used && used >= 0 && used <= memoryTotalBytes ? used : null;
        bool disk = diskFreeBytes is { } free && diskTotalBytes is { } total && free >= 0 && total > 0 && free <= total;
        int? cores = cpuCores is >= 1 and <= MaxCores ? cpuCores : null;
        bool load = IsLoad(loadAverage1) && IsLoad(loadAverage5) && IsLoad(loadAverage15);

        return cpu is null && memory is null && !disk && cores is null && !load
            ? null
            : new HostVitals(
                cpu,
                memory,
                disk ? diskFreeBytes : null,
                disk ? diskTotalBytes : null,
                cores,
                load ? loadAverage1 : null,
                load ? loadAverage5 : null,
                load ? loadAverage15 : null);
    }

    // NaN fails both comparisons, so it is never a load.
    private static bool IsLoad(double? value) => value is >= 0 and <= MaxLoad;
}
