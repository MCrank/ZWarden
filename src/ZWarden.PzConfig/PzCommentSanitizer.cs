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
public sealed record PzSettingHelp(string Text, IReadOnlyList<PzEnumOption> Options);

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

        return new PzSettingHelp(string.Join(' ', textParts), options);
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
