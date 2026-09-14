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
        return PzConfigReadResult.Success(document, backing.Diagnostics);
    }
}
