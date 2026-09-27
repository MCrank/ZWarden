using System.Text.RegularExpressions;

namespace ZWarden.Web.Tests;

/// <summary>
/// Reads the value a browser would submit for a static-SSR <c>&lt;select&gt;</c>. Blazor sets a select's value only
/// through JS on an interactive page, so on a static-SSR re-render (e.g. after a warning) the bound value must be
/// marked on the option itself, or the browser falls back to the first option and silently resubmits it (#258 live
/// pass: the wizard reverted a pinned branch to public). With several options marked, a browser takes the last one.
/// </summary>
internal static partial class SsrSelect
{
    public static string? SelectedValue(string html, string name)
    {
        Match select = Regex.Match(html, $"<select[^>]*name=\"{Regex.Escape(name)}\"[^>]*>([\\s\\S]*?)</select>");
        if (!select.Success)
        {
            return null;
        }

        List<Match> options = OptionTag().Matches(select.Groups[1].Value).ToList();
        Match? chosen = options.LastOrDefault(o => Selected().IsMatch(o.Groups[1].Value)) ?? options.FirstOrDefault();
        return chosen is null ? null : OptionValue().Match(chosen.Groups[1].Value).Groups[1].Value;
    }

    [GeneratedRegex("<option\\b([^>]*)>", RegexOptions.IgnoreCase)]
    private static partial Regex OptionTag();

    [GeneratedRegex("\\bselected\\b", RegexOptions.IgnoreCase)]
    private static partial Regex Selected();

    [GeneratedRegex("value=\"([^\"]*)\"")]
    private static partial Regex OptionValue();
}
