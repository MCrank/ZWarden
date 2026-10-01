namespace ZWarden.Web.Components.Pages.Servers.Sections;

/// <summary>Pure helpers shared by the Server Detail page and more than one of its sections (#298).</summary>
internal static class ServerDetailFormat
{
    // #114: the default warning schedule the operator control uses when "warn players" is left on. The Agent holds
    // the authoritative default for a plain (header-button) restart; this is the schedule the configure form sends.
    private static readonly int[] DefaultRestartWarning = [300, 60, 30, 10];

    /// <summary>
    /// Maps the operator's chosen preset to a descending countdown schedule (#213). "immediate" ⇒ an empty schedule
    /// (no broadcast); anything else warns players for that long before the stop.
    /// </summary>
    public static IReadOnlyList<int> CountdownLeads(string? preset) => preset switch
    {
        "immediate" => [],
        "1m" => [60, 30, 10],
        "15m" => [900, 300, 60, 30, 10],
        _ => DefaultRestartWarning, // "5m" and any unexpected value ⇒ the default five-minute schedule.
    };

    /// <summary>A byte count in binary units, e.g. "1.5 GiB".</summary>
    public static string FormatSize(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} {units[unit]}" : $"{size:0.0} {units[unit]}";
    }
}
