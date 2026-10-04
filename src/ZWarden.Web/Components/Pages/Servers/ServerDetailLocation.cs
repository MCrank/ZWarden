using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;

namespace ZWarden.Web.Components.Pages.Servers;

/// <summary>
/// The Server Detail page's URL and the query values it reads (#312): <c>?section=</c> (#162), <c>?file=</c> and
/// <c>?op=</c> (F20c, #226), <c>?add=</c> (#292). The page keeps its own URL because an in-place switch is a
/// <c>history.pushState</c> the circuit's <c>NavigationManager</c> never sees. Every value is untrusted: the page resolves
/// each one fail-closed.
/// </summary>
public sealed record ServerDetailLocation(string? Path, string? Section, string? File, string? Op, string? Add)
{
    /// <summary>Reads <paramref name="url"/> (absolute); anything else is a location with no page and no values.</summary>
    public static ServerDetailLocation Parse(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return new(null, null, null, null, null);
        }

        Dictionary<string, StringValues> query = QueryHelpers.ParseQuery(uri.Query);
        return new(uri.AbsolutePath, First("section"), First("file"), First("op"), First("add"));

        string? First(string key) => query.TryGetValue(key, out StringValues values) ? values[0] : null;
    }

    /// <summary>Whether this is the Server Detail page of <paramref name="serverId"/> (any query).</summary>
    public bool IsServerPage(string serverId) =>
        Path is not null
        && string.Equals(Path.TrimEnd('/'), $"/servers/{serverId}", StringComparison.OrdinalIgnoreCase);
}
