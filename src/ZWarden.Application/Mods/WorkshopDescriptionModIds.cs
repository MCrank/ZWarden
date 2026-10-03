using System.Text.RegularExpressions;
using ZWarden.Domain.Mods;

namespace ZWarden.Application.Mods;

/// <summary>
/// Pulls the best-guess mod ids out of a Workshop item's description (#290). Steam has no mod-id field, but authors
/// conventionally list <c>Mod ID: …</c> lines; checked against the friends' server on 2026-10-01, 9 of 10 matched
/// the ids on disk exactly. The guess is only a guess: after a boot, <c>mod.info</c> on disk is the truth.
/// </summary>
/// <remarks>
/// Description text is untrusted (trust-boundaries §8). BBCode is stripped; only lines that <em>start</em> with
/// <c>Mod ID:</c> / <c>ModID:</c> / <c>Mod IDs:</c> count; a line may list several ids split on <c>,</c> or
/// <c>;</c>; each candidate keeps only its first word (dropping notes like "(for Build 42)") and, for the B42
/// <c>workshopId/ModId</c> form, the part after the last <c>/</c>. Every candidate must pass <see cref="PzModId"/>;
/// the rest are dropped. De-duplicated ordinally, in first-seen order.
/// </remarks>
public static partial class WorkshopDescriptionModIds
{
    private static readonly char[] CandidateSeparators = [',', ';'];

    /// <summary>Parses the candidate mod ids from <paramref name="description"/>.</summary>
    /// <param name="description">The Workshop description (BBCode), or null when Steam returned none.</param>
    /// <returns>The validated candidates in first-seen order; empty when none are listed.</returns>
    public static IReadOnlyList<PzModId> Parse(string? description)
    {
        if (string.IsNullOrEmpty(description))
        {
            return [];
        }

        List<PzModId> ids = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        string plain = BbCodeTag().Replace(description, string.Empty);

        foreach (string line in plain.Split('\n'))
        {
            Match match = ModIdLine().Match(line);
            if (!match.Success)
            {
                continue;
            }

            foreach (string part in match.Groups["rest"].Value.Split(CandidateSeparators))
            {
                if (Candidate(part) is { } candidate
                    && PzModId.TryCreate(candidate, out PzModId id)
                    && seen.Add(id.Value))
                {
                    ids.Add(id);
                }
            }
        }

        return ids;
    }

    private static string? Candidate(string part)
    {
        string trimmed = part.Trim().Trim('`');
        int space = trimmed.AsSpan().IndexOfAny(" \t\r");
        string word = space < 0 ? trimmed : trimmed[..space];
        int slash = word.LastIndexOf('/');
        string id = slash < 0 ? word : word[(slash + 1)..];
        return id.Length == 0 ? null : id;
    }

    // [b], [/b], [*], [h1], [url=…], [list] … — any bracketed tag, with an optional =argument.
    [GeneratedRegex(@"\[/?[A-Za-z0-9*]+(?:=[^\]\r\n]*)?\]", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex BbCodeTag();

    // A line that starts (after bullets) with "Mod ID:", "ModID:" or "Mod IDs:".
    [GeneratedRegex(@"^[\s*\-•·>]*mod\s*ids?\s*:(?<rest>.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ModIdLine();
}
