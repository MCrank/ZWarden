namespace ZWarden.Application.Workshop;

/// <summary>
/// Which Project Zomboid build a Workshop item supports, from its author's tags (#292). ZWarden runs Build 42 only, so
/// an item tagged "Build 41" but not "Build 42" won't load and the Add sheet blocks its Install. An untagged item is
/// allowed: a missing tag proves nothing. Tags are untrusted text; matching is case-insensitive.
/// </summary>
public static class WorkshopBuildSupport
{
    private const string Build41 = "Build 41";
    private const string Build42 = "Build 42";

    /// <summary>The item claims Build 41 support and not Build 42.</summary>
    public static bool IsBuild41Only(IReadOnlyList<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        return tags.Contains(Build41, StringComparer.OrdinalIgnoreCase)
            && !tags.Contains(Build42, StringComparer.OrdinalIgnoreCase);
    }
}
