namespace ZWarden.PzConfig.Internal;

/// <summary>
/// The hand-written reader for <c>&lt;name&gt;.ini</c>: <c># comment</c> and <c>KEY=value</c> lines,
/// no sections and no continuations (research §7 — the game regenerates the comments on every start,
/// so nothing heavier is warranted, and Loretta has no business here). Every value is a string; the
/// schema is what interprets a value as a bool or a number. The reader is line-independent, so a
/// malformed line becomes a recoverable diagnostic (with its line number) and the rest still reads.
/// </summary>
internal static class IniConfigReader
{
    public static PzConfigReadResult Read(ReadOnlySpan<byte> bytes)
    {
        string text = PzText.DecodeUtf8(bytes);

        // The backing owns the one parse: it builds the value model, the value-span map used for
        // surgical edits (F20b), and the recoverable-line diagnostics.
        var backing = new IniEditBacking(text);
        var document = new PzConfigDocument(PzConfigKind.Ini, backing);
        return PzConfigReadResult.Success(document, backing.Diagnostics, HarvestComments(text));
    }

    // Harvests the '#' comment block sitting directly above each KEY=value line, keyed by the key
    // (first occurrence, matching PzTable.TryGet). A blank line detaches a comment from a later key —
    // it describes nothing directly below it — and a key line resets the pending block. Markers are
    // stripped and consecutive lines joined, mirroring the Lua harvest (F20c).
    private static Dictionary<string, string> HarvestComments(string text)
    {
        var comments = new Dictionary<string, string>(StringComparer.Ordinal);
        var pending = new List<string>();

        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.EndsWith('\r') ? rawLine[..^1] : rawLine;
            string trimmed = line.TrimStart();

            if (trimmed.Length == 0)
            {
                pending.Clear();
                continue;
            }

            if (trimmed[0] == '#')
            {
                string body = trimmed[1..].Trim();
                if (body.Length > 0)
                {
                    pending.Add(body);
                }

                continue;
            }

            int equals = line.IndexOf('=');
            if (equals > 0)
            {
                string key = line[..equals].Trim();
                if (key.Length > 0 && pending.Count > 0 && !comments.ContainsKey(key))
                {
                    comments[key] = string.Join('\n', pending);
                }
            }

            pending.Clear();
        }

        return comments;
    }
}
