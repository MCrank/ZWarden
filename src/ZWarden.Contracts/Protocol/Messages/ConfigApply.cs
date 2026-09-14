using ZWarden.Domain.Configuration;

namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// The scalar kind of a configuration value edit, so the Agent reconstructs the right value node from the wire
/// string without the Contracts assembly depending on <c>ZWarden.PzConfig</c>'s value model. The four PZ config
/// files carry only these three scalar shapes (research §2); a table is never edited surgically (ADR 0010).
/// </summary>
public enum ConfigValueKind
{
    /// <summary>A Lua boolean or a boolean-typed INI value; <see cref="ConfigValueEdit.Value"/> is
    /// <c>"true"</c>/<c>"false"</c>.</summary>
    Bool,

    /// <summary>A number; <see cref="ConfigValueEdit.Value"/> is the exact lexeme to write (e.g. <c>"6"</c> or
    /// <c>"1.0"</c>), whose integer-ness the Agent reads from the presence of a decimal point or exponent — PZ
    /// emits integers as <c>6</c> and doubles as <c>1.0</c> (research §2.1).</summary>
    Number,

    /// <summary>A string; <see cref="ConfigValueEdit.Value"/> is the unescaped content the Agent re-quotes into a
    /// Lua literal (or writes verbatim after <c>=</c> for INI).</summary>
    Text,
}

/// <summary>
/// One surgical value edit: the scalar at a dotted path (e.g. <c>"Map.AllowMiniMap"</c>) is set to
/// <see cref="Value"/>, interpreted per <see cref="Kind"/>. Adding or removing keys is out of scope — the path
/// must already resolve to a scalar in the live file, or the Agent reports the edit as unapplied (ADR 0010).
/// </summary>
/// <param name="Path">The dotted path to the scalar to replace.</param>
/// <param name="Kind">How to interpret <see cref="Value"/>.</param>
/// <param name="Value">The new value in wire form: <c>true</c>/<c>false</c>, a numeric lexeme, or raw string
/// content. Operator input, validated before enqueue; the Agent re-validates and quotes before writing.</param>
public sealed record ConfigValueEdit(string Path, ConfigValueKind Kind, string Value);

/// <summary>
/// Apply surgical value edits to one of a Server's four configuration files (F20b, ADR 0010/0011). The target
/// Server rides the envelope's <see cref="Envelope{TPayload}.ServerId"/>; <see cref="File"/> selects the file,
/// <see cref="Edits"/> are the values to replace, and <see cref="BaselineHash"/> is the drift baseline — the
/// last recorded revision's canonical hash, or <c>null</c> when none has been recorded. It is a
/// <b>mutating, server-scoped</b> Operation (it writes to the <c>/pz/</c> mount), so it claims the per-server
/// lock (ADR 0022). The Agent re-reads and re-parses the live file, compares its canonical value hash against
/// <see cref="BaselineHash"/> and <b>fails closed on a mismatch</b> (a second author changed the file, ADR 0011),
/// then applies each edit as a byte-preserving surgical replacement and writes the result back BOM-less through
/// a temp-file-and-atomic-replace. A successful write reports <see cref="OperationCompleted"/> with a
/// <see cref="ConfigApplyResult"/> (the recorded revision's canonical snapshot + hash); no free-form command
/// crosses the boundary (trust-boundaries.md §9 rule 3).
/// </summary>
/// <param name="File">Which of the Server's four configuration files to write.</param>
/// <param name="BaselineHash">The drift baseline the Agent re-checks before writing (the last revision's hash),
/// or <c>null</c> for the first write to this file.</param>
/// <param name="Edits">The surgical value edits to apply, in the caller's order.</param>
[ProtocolMessage("configuration.apply")]
public sealed record ConfigApply(
    PzConfigFile File,
    string? BaselineHash,
    IReadOnlyList<ConfigValueEdit> Edits) : AgentCommand;
