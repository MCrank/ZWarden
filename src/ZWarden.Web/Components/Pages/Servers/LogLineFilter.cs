using ZWarden.Application.Servers;

namespace ZWarden.Web.Components.Pages.Servers;

/// <summary>
/// The client-side view filter the live-logs panel (F27) applies over its buffered tail: the stdout/stderr stream
/// toggles and a case-insensitive text contains. Extracted as a pure function so the filtering logic is unit-tested
/// directly, without driving the panel's Blueprint inputs through the DOM.
/// </summary>
public static class LogLineFilter
{
    /// <summary>Returns the lines that pass the stream toggles and the text filter, in their existing order.</summary>
    public static IReadOnlyList<ServerLogLineView> Apply(
        IReadOnlyList<ServerLogLineView> lines, bool showStdout, bool showStderr, string filter)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return lines
            .Where(line => line.IsStderr ? showStderr : showStdout)
            .Where(line => string.IsNullOrEmpty(filter) || line.Text.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
