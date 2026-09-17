namespace ZWarden.Web.Components.Layout;

/// <summary>
/// Pure route→chrome helpers for the app shell (ADR 0040). The shell is static SSR, so the top-bar
/// section label is computed per request from the current path rather than pushed by each page. Kept a
/// plain function so it is unit-testable without rendering the layout.
/// </summary>
public static class ShellNavigation
{
    /// <summary>
    /// The section name shown in the top-bar breadcrumb slot for an absolute request path (e.g.
    /// <c>/servers/01ab…</c> → <c>Fleet</c>). Falls back to <c>ZWarden</c> for anything unmapped.
    /// </summary>
    public static string SectionLabelFor(string? absolutePath)
    {
        string path = (absolutePath ?? string.Empty).Trim();
        if (path.Length > 1)
        {
            path = path.TrimEnd('/');
        }

        // Longest-prefix wins is unnecessary here: the app's top-level sections don't nest under one
        // another, so a first-segment match is unambiguous.
        if (path.Length == 0 || path == "/" || IsUnder(path, "/servers"))
        {
            return "Fleet";
        }

        if (IsUnder(path, "/hosts"))
        {
            return "Hosts";
        }

        if (IsUnder(path, "/settings"))
        {
            return "Settings";
        }

        if (IsUnder(path, "/audit"))
        {
            return "Audit";
        }

        if (IsUnder(path, "/account"))
        {
            return "Account";
        }

        if (IsUnder(path, "/setup"))
        {
            return "Setup";
        }

        return "ZWarden";
    }

    private static bool IsUnder(string path, string root) =>
        path.Equals(root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
}
