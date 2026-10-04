using ZWarden.Domain.Ids;

namespace ZWarden.Web.Components.Servers;

/// <summary>
/// How a Host is named to an operator (#336), shared by the Fleet board and the Hosts page: the operator-set Label,
/// else the Agent's self-reported Hostname, else <see cref="ShortId"/>. Label and Hostname are untrusted, observed
/// strings (trust-boundaries.md §3) — display data only, Razor-escaped, never an authorization input.
/// </summary>
public static class HostNames
{
    /// <summary>The Host's display name: Label, else Hostname, else its short id.</summary>
    public static string Display(AgentId id, string? label, string? hostname)
        => !string.IsNullOrWhiteSpace(label) ? label.Trim()
            : !string.IsNullOrWhiteSpace(hostname) ? hostname.Trim()
            : ShortId(id);

    /// <summary>A compact id for a Host with no name (or a caller without <c>Agent.View</c>): the head and the tail,
    /// <c>agt-01a107c8…d8e99</c>. AgentIds are UUIDv7, so Agents enrolled together share the leading hex — the
    /// random tail keeps them apart.</summary>
    public static string ShortId(AgentId id)
    {
        string full = id.ToString();
        return $"{full[..12]}…{full[^5..]}";
    }
}
