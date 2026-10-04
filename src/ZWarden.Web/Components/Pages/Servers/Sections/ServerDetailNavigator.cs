namespace ZWarden.Web.Components.Pages.Servers.Sections;

/// <summary>
/// Moves the Server Detail page to another of its own URLs (another section, file or tracked write) in the circuit
/// (#312), the way a click on one of its links does: the address bar changes and the page switches, with no prerender.
/// The page cascades it to its sections.
/// </summary>
public sealed class ServerDetailNavigator(Func<string, Task> goTo)
{
    /// <summary>Shows <paramref name="href"/> (a link to this Server page).</summary>
    public Task GoToAsync(string href) => goTo(href);
}
