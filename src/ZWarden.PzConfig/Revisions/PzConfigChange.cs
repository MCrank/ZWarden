using ZWarden.PzConfig.Model;

namespace ZWarden.PzConfig.Revisions;

/// <summary>How a value differs between two configuration documents.</summary>
public enum PzConfigChangeKind
{
    /// <summary>The path exists only in the later document.</summary>
    Added,

    /// <summary>The path exists only in the earlier document.</summary>
    Removed,

    /// <summary>The path exists in both but the value (or its shape) differs.</summary>
    Changed,
}

/// <summary>
/// One value-level difference between two configuration documents (F20b, ADR 0011): a dotted path and
/// what its value was and became. This is the unit a Configuration Revision's before/after is rendered
/// in — parsed values, never bytes.
/// </summary>
/// <param name="Path">The dotted path, with <c>[i]</c> segments for positional (spawn) entries.</param>
/// <param name="Kind">Whether the value was added, removed, or changed.</param>
/// <param name="Before">The earlier value, or <see langword="null"/> when <see cref="PzConfigChangeKind.Added"/>.</param>
/// <param name="After">The later value, or <see langword="null"/> when <see cref="PzConfigChangeKind.Removed"/>.</param>
public sealed record PzConfigChange(string Path, PzConfigChangeKind Kind, PzValue? Before, PzValue? After);
