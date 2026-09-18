using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;

namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// One sequenced chunk of the Agent's reply to a live configuration read (F20c, ADR 0041) — an additive
/// <see cref="AgentEvent"/> leaf (ADR 0020, no version bump). A parsed config file (its current values,
/// harvested comment tooltips, and raw text) can exceed SignalR's default per-message ceiling, so the Agent
/// serializes the <see cref="ConfigReadPayload"/> once and splits it into <see cref="ChunkCount"/> ordered chunks;
/// ZWarden.Web reassembles them by <see cref="CorrelationId"/> into a bounded, transient buffer and never persists
/// them (ADR 0011). Observed data (trust §3): a reply is accepted only from the Server's owning Agent and only
/// against a pending read with a matching <see cref="CorrelationId"/> (trust-boundaries.md §8). The chunk body is
/// carried opaquely so the transport is agnostic to the payload shape and cannot be split across a surrogate pair.
/// </summary>
/// <param name="CorrelationId">The opaque id ZWarden.Web sent with the request and the Agent echoes on every
/// chunk, so the reply is matched to its pending read.</param>
/// <param name="ChunkIndex">This chunk's zero-based position in the reply.</param>
/// <param name="ChunkCount">The total number of chunks the reply was split into (≥ 1).</param>
/// <param name="Chunk">This chunk's slice of the reassembled body (see <see cref="ServerConfigContentCodec"/>).</param>
[ProtocolMessage("configuration.read-content")]
public sealed record ServerConfigContent(
    string CorrelationId,
    int ChunkIndex,
    int ChunkCount,
    string Chunk) : AgentEvent;

/// <summary>How a live configuration read turned out on the Agent (F20c). A missing or unparseable file is a
/// first-class status carried on the reply, not an exception (ADR 0010/0041).</summary>
public enum ConfigReadStatus
{
    /// <summary>The file parsed; <see cref="ConfigReadPayload.Settings"/> and <see cref="ConfigReadPayload.RawText"/>
    /// are populated.</summary>
    Read,

    /// <summary>The file exists but did not parse (syntax error or a size/depth pre-check violation);
    /// <see cref="ConfigReadPayload.Diagnostics"/> carries the fatal findings and the raw text is still returned for
    /// the raw view.</summary>
    ParseFailed,

    /// <summary>The file does not exist for this Server yet (it has not been provisioned or started).</summary>
    FileMissing,
}

/// <summary>One diagnostic finding from a live read (F20c) — a parse error or a read-phase note, with a 1-based
/// source position where one applies (ADR 0010). The layer-neutral wire form of a <c>PzConfigDiagnostic</c>.</summary>
/// <param name="Message">The operator-facing message.</param>
/// <param name="Line">The 1-based source line, or <see langword="null"/> when the finding has no position.</param>
/// <param name="Column">The 1-based source column, or <see langword="null"/> when the finding has no position.</param>
public sealed record ConfigReadDiagnostic(string Message, int? Line, int? Column);

/// <summary>
/// One scalar setting the Agent read from the live file (F20c): its dotted <paramref name="Path"/>, current
/// <paramref name="Value"/> in the same wire form the <see cref="ConfigApply"/> edit uses (interpreted per
/// <paramref name="Kind"/>), and its raw leading <paramref name="Comment"/> harvested from the file, or
/// <see langword="null"/> when the setting has none. The comment is locale-generated, attacker-influenced output
/// (PRD 38): it crosses raw and is sanitized on the control plane before it is rendered as data.
/// </summary>
/// <param name="Path">The dotted path to the scalar (e.g. <c>"Map.AllowMiniMap"</c>).</param>
/// <param name="Kind">How to interpret <paramref name="Value"/> — the same three scalar shapes as an edit.</param>
/// <param name="Value">The current value in wire form: <c>true</c>/<c>false</c>, a numeric lexeme, or raw text.</param>
/// <param name="Comment">The raw harvested leading comment, markers stripped and lines joined, or null.</param>
public sealed record ConfigSettingValue(string Path, ConfigValueKind Kind, string Value, string? Comment);

/// <summary>
/// The Agent's structured, layer-neutral view of a live configuration read (F20c, ADR 0041), serialized to
/// canonical JSON and shipped chunked in <see cref="ServerConfigContent"/> sends. It carries the current scalar
/// <see cref="Settings"/>, the whole <see cref="RawText"/> for the advanced raw view, the canonical value-snapshot
/// <see cref="BaselineHash"/> (the drift baseline), and any parse <see cref="Diagnostics"/>. It is <b>not</b> itself
/// a protocol message (no <see cref="ProtocolMessageAttribute"/>): it never travels as an <see cref="Envelope{T}"/>
/// payload, only as the reassembled body of the read-content chunks. The Agent is the single parser; the web tier
/// consumes this typed view, never Lua (ADR 0010).
/// </summary>
/// <param name="ServerId">The Server this read is for — echoed so the reply can be matched to its request.</param>
/// <param name="File">Which of the Server's four configuration files was read.</param>
/// <param name="Status">Whether the file was read, failed to parse, or is missing.</param>
/// <param name="Settings">The current scalar settings, in the file's order; empty on a parse failure or a missing
/// file.</param>
/// <param name="RawText">The whole file text (BOM stripped), for the raw view; empty when the file is missing.</param>
/// <param name="BaselineHash">The canonical value-snapshot hash the drift check fingerprints, or
/// <see langword="null"/> when the file did not parse.</param>
/// <param name="Diagnostics">Parse or read-phase findings; empty on a clean read.</param>
public sealed record ConfigReadPayload(
    ServerId ServerId,
    PzConfigFile File,
    ConfigReadStatus Status,
    IReadOnlyList<ConfigSettingValue> Settings,
    string RawText,
    string? BaselineHash,
    IReadOnlyList<ConfigReadDiagnostic> Diagnostics);
