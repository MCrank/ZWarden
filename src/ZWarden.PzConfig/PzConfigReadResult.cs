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
    private PzConfigReadResult(bool parsed, IPzConfigDocument? document, IReadOnlyList<PzConfigDiagnostic> diagnostics)
    {
        Parsed = parsed;
        Document = document;
        Diagnostics = diagnostics;
    }

    /// <summary><see langword="true"/> when the file parsed and <see cref="Document"/> is non-null.</summary>
    public bool Parsed { get; }

    /// <summary>The parsed document, or <see langword="null"/> when the file did not parse.</summary>
    public IPzConfigDocument? Document { get; }

    /// <summary>
    /// Findings from opening the file. On failure these are the fatal errors (pre-check violation or
    /// syntax error, each with a position where one applies). On success this is empty — validation
    /// findings come separately from <see cref="IPzConfigValidator"/>.
    /// </summary>
    public IReadOnlyList<PzConfigDiagnostic> Diagnostics { get; }

    /// <summary>A successful open.</summary>
    public static PzConfigReadResult Success(IPzConfigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new PzConfigReadResult(parsed: true, document, ReadOnlyCollection<PzConfigDiagnostic>.Empty);
    }

    /// <summary>A failed open carrying the fatal diagnostics.</summary>
    public static PzConfigReadResult Failure(params IReadOnlyList<PzConfigDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (diagnostics.Count == 0)
        {
            throw new ArgumentException("A failed read must carry at least one diagnostic.", nameof(diagnostics));
        }

        return new PzConfigReadResult(parsed: false, document: null, [.. diagnostics]);
    }
}
