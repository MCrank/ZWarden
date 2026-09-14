using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;

namespace ZWarden.Application.Configuration;

/// <summary>How a value differs between a revision and the one before it (F20b PR-4). Mirrors the library's
/// change kinds in a layer-neutral form so no parser type crosses into the UI.</summary>
public enum ConfigChangeKind
{
    /// <summary>The path exists only in this revision.</summary>
    Added,

    /// <summary>The path existed only in the previous revision.</summary>
    Removed,

    /// <summary>The path exists in both but its value differs.</summary>
    Changed,
}

/// <summary>One value-level difference between a revision and its predecessor, rendered for display (F20b PR-4).
/// The values are already-rendered strings — a boolean literal, a numeric lexeme, or the string content — so the
/// UI never sees a parser value type. Untrusted only insofar as the operator authored it; escaped at render.</summary>
/// <param name="Path">The dotted path, with <c>[i]</c> segments for positional (spawn) entries.</param>
/// <param name="Kind">Whether the value was added, removed, or changed.</param>
/// <param name="Before">The previous value rendered as text, or <c>null</c> when <see cref="ConfigChangeKind.Added"/>.</param>
/// <param name="After">The new value rendered as text, or <c>null</c> when <see cref="ConfigChangeKind.Removed"/>.</param>
public sealed record ConfigValueChange(string Path, ConfigChangeKind Kind, string? Before, string? After);

/// <summary>
/// A Configuration Revision shaped for the history UI (F20b PR-4): its identity and recorded time, a short hash
/// for display, whether it is the current (most recent) revision, and the value-level changes it introduced
/// relative to the revision before it (ADR 0011 — parsed values, never bytes). Layer-neutral; carries no parser
/// type. Restore targets a revision by <see cref="Id"/>.
/// </summary>
/// <param name="Id">The revision identifier (the restore target).</param>
/// <param name="File">Which of the Server's four files this revision is for.</param>
/// <param name="RecordedAt">When the revision was recorded (UTC).</param>
/// <param name="ShortHash">The first 12 hex characters of the snapshot hash, for display.</param>
/// <param name="IsCurrent">True for the most recent revision — the file's current recorded state (not restorable).</param>
/// <param name="Changes">The value-level diff from the preceding revision; empty for the earliest (baseline) revision.</param>
public sealed record ConfigRevisionView(
    ConfigurationRevisionId Id,
    PzConfigFile File,
    DateTimeOffset RecordedAt,
    string ShortHash,
    bool IsCurrent,
    IReadOnlyList<ConfigValueChange> Changes);
