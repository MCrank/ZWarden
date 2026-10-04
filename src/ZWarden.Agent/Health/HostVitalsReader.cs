using System.Globalization;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;

namespace ZWarden.Agent.Health;

/// <summary>The host's live vitals for the Hosts card meters (#170). Each figure is <c>null</c> when unknown.</summary>
/// <param name="CpuPercent">Host-wide CPU busy % since the previous read.</param>
/// <param name="MemoryUsedBytes">Host RAM in use (<c>MemTotal − MemAvailable</c>).</param>
/// <param name="DiskFreeBytes">Free space the Agent can write on the volume holding the PZ data root.</param>
/// <param name="DiskTotalBytes">The total size of that volume.</param>
/// <param name="CpuCores">The host's logical CPUs (one <c>cpuN</c> line each in <c>/proc/stat</c>).</param>
/// <param name="LoadAverage1">The 1-minute load average from <c>/proc/loadavg</c>.</param>
/// <param name="LoadAverage5">The 5-minute load average.</param>
/// <param name="LoadAverage15">The 15-minute load average.</param>
public readonly record struct HostVitals(
    double? CpuPercent,
    long? MemoryUsedBytes,
    long? DiskFreeBytes,
    long? DiskTotalBytes,
    int? CpuCores = null,
    double? LoadAverage1 = null,
    double? LoadAverage5 = null,
    double? LoadAverage15 = null);

/// <summary>Reads the host's vitals (#170). Fail-soft: a figure it can't read is <c>null</c>, never an exception.</summary>
public interface IHostVitalsReader
{
    /// <summary>Reads the current vitals. CPU is the busy share since the previous call, so the first call has none.</summary>
    HostVitals Read();
}

/// <summary>
/// The default <see cref="IHostVitalsReader"/> (#170). The Agent runs in a Linux container, where <c>/proc/stat</c> and
/// <c>/proc/meminfo</c> are not namespaced, so they describe the whole host — no extra mount or privilege. The disk
/// figures come from <see cref="DriveInfo"/> on the data root itself, which on Unix is a <c>statvfs</c> of the host
/// volume behind the bind mount (not the path root, which would be the container's overlay). Without <c>/proc</c>
/// (a Windows dev box) CPU, memory, cores and load are simply unknown. Every value is observed, untrusted data for ZWarden.Web.
/// </summary>
public sealed class HostVitalsReader : IHostVitalsReader
{
    private readonly string _procRoot;
    private readonly string _dataRoot;
    private readonly Lock _gate = new();
    private CpuTimes? _previous;

    public HostVitalsReader(IOptions<AgentOptions> options, string procRoot = "/proc")
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(procRoot);
        _dataRoot = options.Value.DataMountRoot;
        _procRoot = procRoot;
    }

    /// <inheritdoc />
    public HostVitals Read()
    {
        string[]? stat = ReadLines("stat");
        (long? free, long? total) = ReadDisk();
        (double? load1, double? load5, double? load15) = ReadLoadAverages();
        return new HostVitals(
            ReadCpuPercent(stat), ReadMemoryUsed(), free, total, CountCores(stat), load1, load5, load15);
    }

    // One "cpuN" line per logical CPU (the aggregate line is "cpu " with no digit).
    private static int? CountCores(string[]? stat)
    {
        int cores = stat?.Count(l => l.Length > 3 && l.StartsWith("cpu", StringComparison.Ordinal) && char.IsAsciiDigit(l[3])) ?? 0;
        return cores > 0 ? cores : null;
    }

    // "0.12 0.20 0.18 1/234 5678": the first three fields are the 1, 5 and 15 minute load averages.
    private (double? One, double? Five, double? Fifteen) ReadLoadAverages()
    {
        string[]? fields = ReadLines("loadavg")?.FirstOrDefault()?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields is not { Length: >= 3 }
            || !double.TryParse(fields[0], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double one)
            || !double.TryParse(fields[1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double five)
            || !double.TryParse(fields[2], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double fifteen))
        {
            return (null, null, null);
        }

        return (one, five, fifteen);
    }

    private double? ReadCpuPercent(string[]? stat)
    {
        CpuTimes? current = ReadCpuTimes(stat);
        lock (_gate)
        {
            CpuTimes? previous = _previous;
            _previous = current;
            if (current is not { } now || previous is not { } before)
            {
                return null;
            }

            long total = now.Total - before.Total;
            long idle = now.Idle - before.Idle;
            // No time passed, or a counter went backwards (a reset): unknown rather than a nonsense share.
            if (total <= 0 || idle < 0 || idle > total)
            {
                return null;
            }

            return Math.Round((total - idle) * 100.0 / total, 1);
        }
    }

    // The aggregate "cpu" line: user nice system idle iowait irq softirq steal [guest guest_nice]. Guest time is already
    // inside user/nice, so the total is the first eight; idle includes iowait (the CPU had nothing to run).
    private static CpuTimes? ReadCpuTimes(string[]? stat)
    {
        string? line = stat?.FirstOrDefault(l => l.StartsWith("cpu ", StringComparison.Ordinal));
        if (line is null)
        {
            return null;
        }

        string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 9)
        {
            return null;
        }

        long[] values = new long[8];
        for (int i = 0; i < values.Length; i++)
        {
            if (!long.TryParse(fields[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out values[i]))
            {
                return null;
            }
        }

        return new CpuTimes(values.Sum(), values[3] + values[4]);
    }

    private long? ReadMemoryUsed()
    {
        string[]? lines = ReadLines("meminfo");
        if (lines is null
            || MemInfoKiB(lines, "MemTotal:") is not { } total
            || MemInfoKiB(lines, "MemAvailable:") is not { } available
            || available > total)
        {
            return null;
        }

        return (total - available) * 1024;
    }

    private static long? MemInfoKiB(string[] lines, string key)
    {
        string? line = lines.FirstOrDefault(l => l.StartsWith(key, StringComparison.Ordinal));
        string[]? fields = line?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return fields is { Length: >= 2 }
            && long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out long kib)
            ? kib
            : null;
    }

    private (long? Free, long? Total) ReadDisk()
    {
        try
        {
            if (!Directory.Exists(_dataRoot))
            {
                return (null, null);
            }

            DriveInfo drive = new(Path.GetFullPath(_dataRoot));
            return drive.IsReady ? (drive.AvailableFreeSpace, drive.TotalSize) : (null, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return (null, null);
        }
    }

    private string[]? ReadLines(string name)
    {
        try
        {
            string path = Path.Combine(_procRoot, name);
            return File.Exists(path) ? File.ReadAllLines(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private readonly record struct CpuTimes(long Total, long Idle);
}
