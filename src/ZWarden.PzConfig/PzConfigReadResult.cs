using System.Collections.ObjectModel;

namespace ZWarden.PzConfig;

/// <summary>
/// The outcome of opening a config file: <em>either</em> a parsed <see cref="Document"/> <em>or</em>
/// a not-parsed state carrying the fatal <see cref="Diagnostics"/>. "The file did not parse" is thus
/// a first-class value an operator surface can render (with line and column), not an exception a
/// caller must catch (ADR 0010).
/// </summary>
public sealed class PzConfigReadResult
{
    private static readonly IReadOnlyDictionary<string, string> NoComments =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(0));

    private PzConfigReadResult(
        bool parsed,
        IPzConfigDocument? document,
        IReadOnlyList<PzConfigDiagnostic> diagnostics,
        IReadOnlyDictionary<string, string> comments)
    {
        Parsed = parsed;
        Document = document;
        Diagnostics = diagnostics;
        Comments = comments;
    }

    /// <summary><see langword="true"/> when the file parsed and <see cref="Document"/> is non-null.</summary>
    public bool Parsed { get; }

    /// <summary>The parsed document, or <see langword="null"/> when the file did not parse.</summary>
    public IPzConfigDocument? Document { get; }

    /// <summary>
    /// Findings from opening the file. On failure these are the fatal errors (pre-check violation or
    /// syntax error, each with a position where one applies). On success these are read-phase notes
    /// such as a malformed line the reader recovered from — <em>semantic</em> validation (types,
    /// ranges, unknown keys) comes separately from <see cref="IPzConfigValidator"/>.
    /// </summary>
    public IReadOnlyList<PzConfigDiagnostic> Diagnostics { get; }

    /// <summary>
    /// Each setting's leading comment, keyed by dotted path (e.g. <c>"Map.AllowMiniMap"</c>), with the
    /// <c>--</c> / <c>#</c> markers stripped and consecutive lines joined by newlines. In a Project
    /// Zomboid sandbox file this comment block <em>is</em> the setting's in-game tooltip (research
    /// §2.1). Deliberately a side-map, not a field on the value model: comments are locale-generated
    /// output, never content — they are outside the revision snapshot, diff and drift check (ADR 0011).
    /// Empty (never <see langword="null"/>) when the file carries none. The raw text here is turned
    /// into display help by the comment sanitizer.
    /// </summary>
    public IReadOnlyDictionary<string, string> Comments { get; }

    /// <summary>A successful open, optionally carrying non-fatal read-phase diagnostics and comments.</summary>
    public static PzConfigReadResult Success(
        IPzConfigDocument document,
        IReadOnlyList<PzConfigDiagnostic>? diagnostics = null,
        IReadOnlyDictionary<string, string>? comments = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new PzConfigReadResult(
            parsed: true,
            document,
            diagnostics is { Count: > 0 } ? [.. diagnostics] : ReadOnlyCollection<PzConfigDiagnostic>.Empty,
            comments is { Count: > 0 } ? new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(comments, StringComparer.Ordinal)) : NoComments);
    }

    /// <summary>A failed open carrying the fatal diagnostics.</summary>
    public static PzConfigReadResult Failure(params IReadOnlyList<PzConfigDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (diagnostics.Count == 0)
        {
            throw new ArgumentException("A failed read must carry at least one diagnostic.", nameof(diagnostics));
        }

        return new PzConfigReadResult(parsed: false, document: null, [.. diagnostics], NoComments);
    }
}
