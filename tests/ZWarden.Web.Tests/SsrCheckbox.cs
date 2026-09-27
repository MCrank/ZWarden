using System.Text.RegularExpressions;

namespace ZWarden.Web.Tests;

/// <summary>
/// Asserts a static-SSR page renders a checkbox an operator can actually tick. BbCheckbox renders a
/// <c>&lt;button role="checkbox"&gt;</c> that only toggles with a circuit (plus a hidden mirror input), so on a
/// static-SSR form it is dead — while a test that POSTs the field directly still passes. The control must be a native
/// <c>&lt;input type="checkbox"&gt;</c> carrying the label's id and the bound name.
/// </summary>
internal static partial class SsrCheckbox
{
    public static bool IsNative(string html, string id, string name) =>
        InputTag().Matches(html).Any(m =>
            m.Value.Contains("type=\"checkbox\"", StringComparison.Ordinal)
            && m.Value.Contains($"id=\"{id}\"", StringComparison.Ordinal)
            && m.Value.Contains($"name=\"{name}\"", StringComparison.Ordinal)
            && m.Value.Contains("value=\"true\"", StringComparison.Ordinal)
            && !m.Value.Contains("hidden", StringComparison.Ordinal)
            && !m.Value.Contains("tabindex=\"-1\"", StringComparison.Ordinal));

    [GeneratedRegex("<input\\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex InputTag();
}
