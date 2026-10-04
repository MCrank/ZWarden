using ZWarden.Application.Servers;
using ZWarden.Web.Components.Pages.Servers.Sections;
using ZWarden.Web.Components.Servers;

namespace ZWarden.Web.Components.Hosts;

/// <summary>
/// One Host card's telemetry (#170): the latest vitals from <see cref="IHostCapacityCache"/>, projected for the first
/// render of <c>/hosts</c> and for the <c>/api/hosts/telemetry</c> poll that <c>live-status.js</c> applies, so the two
/// agree. The texts and the age are formatted here against the server clock. Every value is untrusted Agent data
/// (trust-boundaries.md §3), shown as text only.
/// </summary>
/// <param name="CpuPercent">Host-wide CPU busy %; <c>null</c> shows —.</param>
/// <param name="MemoryUsedBytes">Host RAM in use.</param>
/// <param name="MemoryTotalBytes">Host RAM in total (from the same report).</param>
/// <param name="DiskUsedBytes">Used space on the PZ data volume (total − free), for the meter.</param>
/// <param name="DiskTotalBytes">The PZ data volume's size.</param>
/// <param name="MemoryText">e.g. <c>12.0 GiB / 32.0 GiB</c>, or —.</param>
/// <param name="DiskText">e.g. <c>212.0 GiB free of 480.0 GiB</c>, or —.</param>
/// <param name="Age">How old the report is (<c>as of 2 min ago</c>), prefixed <c>stale · </c> once stale.</param>
/// <param name="Stale">The report is at least <see cref="StaleAfter"/> old: the Agent has stopped sending it.</param>
public sealed record HostTelemetry(
    double? CpuPercent,
    long? MemoryUsedBytes,
    long? MemoryTotalBytes,
    long? DiskUsedBytes,
    long? DiskTotalBytes,
    string MemoryText,
    string DiskText,
    string Age,
    bool Stale)
{
    /// <summary>Four missed reports at the Agent's default 15 s cadence.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(60);

    /// <summary>The card's telemetry, or <c>null</c> for an unreachable host (it shows none). <paramref name="capacity"/>
    /// must be the host's own latest report (the cache is keyed by the reporting connection's Agent).</summary>
    public static HostTelemetry? For(HostCapacity? capacity, bool connected, DateTimeOffset now)
    {
        if (!connected)
        {
            return null;
        }

        if (capacity is null)
        {
            return new HostTelemetry(null, null, null, null, null, FleetFacts.Dash, FleetFacts.Dash, "no report yet", false);
        }

        HostVitals? v = capacity.Vitals;
        long? memoryUsed = v?.MemoryUsedBytes;
        long? memoryTotal = memoryUsed is null || capacity.TotalBytes <= 0 ? null : capacity.TotalBytes;
        long? diskFree = v?.DiskFreeBytes;
        long? diskTotal = diskFree is null ? null : v!.DiskTotalBytes;
        bool stale = now - capacity.ReportedAt >= StaleAfter;
        string age = FleetFacts.FormatSampleAge(capacity.ReportedAt, now);

        return new HostTelemetry(
            v?.CpuPercent,
            memoryTotal is null ? null : memoryUsed,
            memoryTotal,
            diskTotal is null ? null : diskTotal - diskFree,
            diskTotal,
            memoryTotal is null ? FleetFacts.Dash : $"{Size(memoryUsed!.Value)} / {Size(memoryTotal.Value)}",
            diskTotal is null ? FleetFacts.Dash : $"{Size(diskFree!.Value)} free of {Size(diskTotal.Value)}",
            stale ? $"stale · {age}" : age,
            stale);
    }

    private static string Size(long bytes) => ServerDetailFormat.FormatSize(bytes);
}
