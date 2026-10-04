using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace ZWarden.Agent.SteamCmd;

/// <summary>
/// Reads the one fact #275 needs from Steam's Workshop manifest (<c>appworkshop_108600.acf</c>, Valve's KeyValues
/// format): the Steam <c>timeupdated</c> of the copy of each Workshop item installed on disk, from the
/// <c>WorkshopItemsInstalled</c> block. It equals the item's Steam <c>time_updated</c> at download, and PZ rewrites it
/// when a boot pulls a newer version (slice 0 spike), so comparing it with Steam's current value says whether a
/// restart would pull an update. A pure text function so it is unit-tested without any Steam files; the content is
/// untrusted PZ/Steam output, so anything that doesn't parse cleanly yields nothing rather than throwing.
/// </summary>
public static class SteamWorkshopManifest
{
    private const string InstalledBlockKey = "WorkshopItemsInstalled";
    private const string TimeUpdatedKey = "timeupdated";
    private const int MaxDepth = 16;
    private const int MaxWorkshopIdLength = 20;

    /// <summary>Maps each installed item's Workshop id (numeric) to its <c>timeupdated</c>. Items with no, zero, or
    /// out-of-range <c>timeupdated</c>, and non-numeric ids, are skipped; unparseable text yields an empty map. The
    /// file can still list items whose folders were deleted (F293), so callers join it on what's on disk.</summary>
    public static IReadOnlyDictionary<string, DateTimeOffset> ParseInstalledTimeUpdated(string manifestContent)
    {
        ArgumentNullException.ThrowIfNull(manifestContent);
        Dictionary<string, DateTimeOffset> times = new(StringComparer.Ordinal);
        if (!TryParse(manifestContent, out Block? root)
            || root.Entries is not [{ Child: { } appWorkshop }]
            || appWorkshop.FindBlock(InstalledBlockKey) is not { } installed)
        {
            return times;
        }

        foreach (Entry entry in installed.Entries)
        {
            if (entry.Child?.FindText(TimeUpdatedKey) is { } text
                && IsWorkshopId(entry.Key)
                && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long seconds)
                && seconds > 0
                && seconds <= DateTimeOffset.MaxValue.ToUnixTimeSeconds())
            {
                times[entry.Key] = DateTimeOffset.FromUnixTimeSeconds(seconds);
            }
        }

        return times;
    }

    private static bool IsWorkshopId(string value) =>
        value.Length is > 0 and <= MaxWorkshopIdLength && value.All(char.IsAsciiDigit);

    private static bool TryParse(string text, [NotNullWhen(true)] out Block? root)
    {
        root = null;
        if (Tokenize(text) is not { } tokens)
        {
            return false;
        }

        int position = 0;
        root = ParseBlock(tokens, ref position, depth: 0);
        return root is not null;
    }

    // A block is a run of `"key" "value"` or `"key" { block }` pairs. A nested block ends at its `}`; the top level
    // (depth 0) ends at the end of input. Anything else is malformed.
    private static Block? ParseBlock(List<Token> tokens, ref int position, int depth)
    {
        if (depth > MaxDepth)
        {
            return null;
        }

        Block block = new();
        while (position < tokens.Count)
        {
            Token key = tokens[position++];
            if (key.Kind == TokenKind.Close)
            {
                return depth > 0 ? block : null;
            }

            if (key.Kind != TokenKind.Text || position >= tokens.Count)
            {
                return null;
            }

            Token next = tokens[position++];
            if (next.Kind == TokenKind.Text)
            {
                block.Entries.Add(new Entry(key.Text, next.Text, null));
            }
            else if (next.Kind == TokenKind.Open && ParseBlock(tokens, ref position, depth + 1) is { } child)
            {
                block.Entries.Add(new Entry(key.Text, null, child));
            }
            else
            {
                return null;
            }
        }

        return depth == 0 ? block : null;
    }

    private static List<Token>? Tokenize(string text)
    {
        List<Token> tokens = [];
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c == '{')
            {
                tokens.Add(new Token(TokenKind.Open, string.Empty));
                i++;
            }
            else if (c == '}')
            {
                tokens.Add(new Token(TokenKind.Close, string.Empty));
                i++;
            }
            else if (c == '"')
            {
                StringBuilder value = new();
                i++;
                while (i < text.Length && text[i] != '"')
                {
                    if (text[i] == '\\' && i + 1 < text.Length)
                    {
                        i++;
                    }

                    value.Append(text[i++]);
                }

                if (i >= text.Length)
                {
                    return null;
                }

                i++;
                tokens.Add(new Token(TokenKind.Text, value.ToString()));
            }
            else
            {
                int start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('{' or '}' or '"'))
                {
                    i++;
                }

                tokens.Add(new Token(TokenKind.Text, text[start..i]));
            }
        }

        return tokens;
    }

    private enum TokenKind
    {
        Text,
        Open,
        Close,
    }

    private readonly record struct Token(TokenKind Kind, string Text);

    private sealed record Entry(string Key, string? Text, Block? Child);

    private sealed class Block
    {
        public List<Entry> Entries { get; } = [];

        public Block? FindBlock(string key) =>
            Entries.FirstOrDefault(e => e.Child is not null && string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase))?.Child;

        public string? FindText(string key) =>
            Entries.FirstOrDefault(e => e.Text is not null && string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase))?.Text;
    }
}
