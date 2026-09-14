using ZWarden.PzConfig.Model;

namespace ZWarden.PzConfig;

/// <summary>
/// The narrow, ZWarden-owned seam over one parsed configuration file (ADR 0010). It is a rule, not a
/// convenience: nothing library-shaped crosses it, so the Loretta parser is one implementation
/// behind this interface and the hand-rolled reader stays a viable fallback.
/// </summary>
/// <remarks>
/// F20a delivers the <em>read</em> half — open, read values. The <em>write</em> half the ADR
/// describes (set a value, emit bytes) belongs to F20b and extends this interface then; it is left
/// out here rather than stubbed so F20a ships no throwing surface.
/// </remarks>
public interface IPzConfigDocument
{
    /// <summary>Which config file this is.</summary>
    PzConfigKind Kind { get; }

    /// <summary>
    /// The root table: the table assigned to <c>SandboxVars</c>, the table the spawn function
    /// returns, or the flat set of INI key=value entries. Never null — a document only exists for a
    /// file that parsed.
    /// </summary>
    PzTable Root { get; }

    /// <summary>
    /// Reads a value by dotted path from the root (e.g. <c>"Map.AllowMiniMap"</c>). Each segment
    /// selects a named entry; descending requires the intermediate value to be a table. Returns
    /// <see langword="false"/> (not an exception) when any segment is absent or not a table.
    /// </summary>
    bool TryGetValue(string path, out PzValue value);
}
