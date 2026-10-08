namespace ZWarden.Web.Components.Pages.Settings;

/// <summary>
/// The Settings page's URL (#344): <c>/settings</c> or <c>/settings/{section}</c>. The page keeps its own URL because an
/// in-place section switch is a <c>history.pushState</c> the circuit's <c>NavigationManager</c> never sees (#312). The
/// section is untrusted: the page resolves it fail-closed.
/// </summary>
public sealed record SettingsLocation(bool IsSettingsPage, string? Section)
{
    /// <summary>Reads <paramref name="url"/> (absolute); anything else is not the Settings page.</summary>
    public static SettingsLocation Parse(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return new(false, null);
        }

        string[] segments = uri.AbsolutePath.Trim('/').Split('/');
        if (!string.Equals(segments[0], "settings", StringComparison.OrdinalIgnoreCase) || segments.Length > 2)
        {
            return new(false, null);
        }

        return new(true, segments.Length == 2 ? Uri.UnescapeDataString(segments[1]).ToLowerInvariant() : null);
    }
}
