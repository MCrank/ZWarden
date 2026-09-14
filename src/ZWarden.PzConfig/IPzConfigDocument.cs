using ZWarden.PzConfig.Model;

namespace ZWarden.PzConfig;

/// <summary>
/// The narrow, ZWarden-owned seam over one parsed configuration file (ADR 0010). It is a rule, not a
/// convenience: nothing library-shaped crosses it, so the Loretta parser is one implementation
/// behind this interface and the hand-rolled reader stays a viable fallback.
/// </summary>
/// <remarks>
/// F20a delivered the <em>read</em> half — open, read values. F20b adds the <em>write</em> half the
/// ADR describes: <see cref="TrySetValue"/> (a surgical, single-value replacement) and
/// <see cref="Emit"/> (BOM-less bytes). Both are first-class rather than throwing: an edit that cannot
/// apply returns a <see cref="PzConfigEditResult"/>, and only an <see cref="IsEditable"/> document can
/// emit. A document constructed directly from a value model (no parse backing) is read-only.
/// </remarks>
public interface IPzConfigDocument
{
    /// <summary>Which config file this is.</summary>
    PzConfigKind Kind { get; }

    /// <summary>
    /// The root table: the table assigned to <c>SandboxVars</c>, the table the spawn function
    /// returns, or the flat set of INI key=value entries. Never null — a document only exists for a
    /// file that parsed. Reflects any applied <see cref="TrySetValue"/> edits.
    /// </summary>
    PzTable Root { get; }

    /// <summary>
    /// <see langword="true"/> when this document was opened from bytes and retains the parse backing
    /// needed to edit and emit. A document built directly from a value model is read-only.
    /// </summary>
    bool IsEditable { get; }

    /// <summary>
    /// Reads a value by dotted path from the root (e.g. <c>"Map.AllowMiniMap"</c>). Each segment
    /// selects a named entry; descending requires the intermediate value to be a table. Returns
    /// <see langword="false"/> (not an exception) when any segment is absent or not a table.
    /// </summary>
    bool TryGetValue(string path, out PzValue value);

    /// <summary>
    /// Replaces the scalar value at a dotted path in place, preserving every other byte of the file
    /// (comments, spacing, key form, line endings). Adding or removing keys is out of scope — the
    /// path must already resolve to a scalar. Returns a <see cref="PzConfigEditResult"/> describing
    /// why the edit did or did not apply; it never throws for an absent path, a table target, or a
    /// non-editable document.
    /// </summary>
    PzConfigEditResult TrySetValue(string path, PzValue newValue);

    /// <summary>
    /// Serializes the document to BOM-less UTF-8, byte-identical to the source except where
    /// <see cref="TrySetValue"/> changed a value (ADR 0010: a UTF-8 BOM is fatal to PZ's lexer, so one
    /// is never written). Throws <see cref="InvalidOperationException"/> if the document
    /// <see cref="IsEditable">is not editable</see>; regenerating a file from the model is forbidden.
    /// </summary>
    byte[] Emit();
}
