using ZWarden.Application.Servers;

namespace ZWarden.Web.Components.Servers;

/// <summary>One Host's unmanaged containers for the Fleet adopt callout (#339): the Host's operator-facing name (#336)
/// and how many PZ containers it reports with no Server record. Serializable: it crosses into an interactive island.</summary>
public sealed record UnmanagedHost(string Name, int Count)
{
    /// <summary>The callout's Hosts, named (Label, else Hostname, else short id — the names reach only a caller who may
    /// view Hosts) and ordered by name, so the page's first render and the island's re-reads compare equal (#357).</summary>
    public static List<UnmanagedHost> From(IEnumerable<UnmanagedHostCount> counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        return [.. counts
            .Select(c => new UnmanagedHost(HostNames.Display(c.AgentId, c.Label, c.Hostname), c.Count))
            .OrderBy(h => h.Name, StringComparer.OrdinalIgnoreCase)];
    }
}
