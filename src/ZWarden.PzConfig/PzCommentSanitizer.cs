using System.Globalization;
using System.Text.RegularExpressions;

namespace ZWarden.PzConfig;

/// <summary>One selectable option parsed from a setting's comment, e.g. <c>1 = Insane</c>.</summary>
/// <param name="Value">The literal on the left of <c>=</c> (the value written to the file, e.g. <c>"1"</c>).</param>
/// <param name="Label">The human label on the right (e.g. <c>"Insane"</c>).</param>
public sealed record PzEnumOption(string Value, string Label);

/// <summary>
/// A setting's display help, derived from its harvested comment: prose with Project Zomboid's UI
/// rich-text markup removed, plus any enum options the comment enumerated. Either part may be empty.
/// </summary>
/// <param name="Text">The description, markup-stripped and whitespace-collapsed; empty when the comment was only options or only markup.</param>
/// <param name="Options">The <c>N = Label</c> options, in file order; empty when the setting is not an enumeration.</param>
public sealed record PzSettingHelp(string Text, IReadOnlyList<PzEnumOption> Options)
{
    /// <summary>The inclusive minimum the comment states (<c>Min: 0</c>), or null when it states none.</summary>
    public double? Min { get; init; }

    /// <summary>The inclusive maximum the comment states (<c>Max: 1000</c>), or null when it states none.</summary>
    public double? Max { get; init; }

    /// <summary>The default the comment states, verbatim (<c>Default: 1.00</c> → <c>"1.00"</c>), or null.</summary>
    public string? Default { get; init; }
}

/// <summary>
/// Turns a harvested raw comment (see <see cref="PzConfigReadResult.Comments"/>) into
/// <see cref="PzSettingHelp"/>. The comment block above a PZ sandbox setting <em>is</em> its in-game
/// tooltip (research §2.1) but it carries UI rich-text markup (<c>&lt;BHC&gt;</c>, <c>&lt;RGB:r,g,b&gt;</c>,
/// <c>[!]</c>) and mixes the description with enum-label lines. This pulls the two apart so an editor can
/// render a labelled control and a clean tooltip. Pure and side-effect free — it runs wherever the read
/// path does (agent-side in F20c PR-B).
/// </summary>
public static class PzCommentSanitizer
{
    // A line that enumerates a value: "<number> = <label>". "Default = Normal" is deliberately not
    // matched — the left side must be digits — so it stays in the prose.
    private static readonly Regex EnumLine = new(@"^\s*(\d+)\s*=\s*(.+?)\s*$", RegexOptions.Compiled);

    // PZ UI markup: any <…> tag (e.g. <BHC>, <RGB:1,1,1>, <LINE>).
    private static readonly Regex Tag = new(@"<[^>]*>", RegexOptions.Compiled);

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    // PZ's generated range phrase on a numeric setting, in both files: "Min: 0 Max: 1000 Default: 2" (#227).
    private static readonly Regex Range = new(
        @"\bMin:\s*(-?\d+(?:\.\d+)?)\s+Max:\s*(-?\d+(?:\.\d+)?)(?:\s+Default:\s*(-?\d+(?:\.\d+)?))?",
        RegexOptions.Compiled);

    /// <summary>Parses a harvested comment into display help. Never throws on shape; empty in, empty out.</summary>
    public static PzSettingHelp Sanitize(string rawComment)
    {
        ArgumentNullException.ThrowIfNull(rawComment);

        var options = new List<PzEnumOption>();
        var textParts = new List<string>();

        foreach (string line in rawComment.Split('\n'))
        {
            Match match = EnumLine.Match(line);
            if (match.Success)
            {
                options.Add(new PzEnumOption(match.Groups[1].Value, CleanInline(match.Groups[2].Value)));
            }
            else
            {
                string text = CleanInline(line);
                if (text.Length > 0)
                {
                    textParts.Add(text);
                }
            }
        }

        // Lift the range phrase out of the prose: the editor shows it as the setting's range and default, so leaving
        // it in the tooltip would say it twice.
        string prose = string.Join(' ', textParts);
        Match range = Range.Match(prose);
        if (!range.Success)
        {
            return new PzSettingHelp(prose, options);
        }

        return new PzSettingHelp(Whitespace.Replace(prose.Remove(range.Index, range.Length), " ").Trim(), options)
        {
            Min = double.Parse(range.Groups[1].Value, CultureInfo.InvariantCulture),
            Max = double.Parse(range.Groups[2].Value, CultureInfo.InvariantCulture),
            Default = range.Groups[3].Success ? range.Groups[3].Value : null,
        };
    }

    // Removes markup and collapses runs of whitespace to a single space.
    private static string CleanInline(string value)
    {
        value = Tag.Replace(value, " ");
        value = value.Replace("[!]", " ", StringComparison.Ordinal);
        value = Whitespace.Replace(value, " ");
        return value.Trim();
    }
}
