namespace ZWarden.Contracts.Protocol;

/// <summary>
/// One sequenced chunk of an operator-authored whole-file configuration edit on its way <b>up</b> to the Agent
/// (F20c PR-D, ADR 0042) — the reverse-direction sibling of the read reply's <c>ServerConfigContent</c>. A raw
/// edit's text can exceed SignalR's default per-message ceiling, so ZWarden.Web serializes it once, Base64-encodes
/// it (so a chunk boundary never falls inside a multi-byte character), and splits it into
/// <see cref="ChunkCount"/> ordered chunks it sends over the <see cref="AgentHubProtocol.StageServerConfigRawEdit"/>
/// channel; the Agent reassembles them by <see cref="CorrelationId"/> into a bounded, transient buffer. This is a
/// plain codec DTO carried as bare hub-method arguments — deliberately <b>not</b> a protocol message
/// (<c>AgentCommand</c>/<c>AgentEvent</c>), so it never enters the closed vocabulary (ADR 0020), exactly like the
/// read request.
/// </summary>
/// <param name="CorrelationId">The id ZWarden.Web stages under and later names on the <c>ConfigApplyRaw</c>
/// Operation, so the Agent matches the staged text to the Operation that applies it.</param>
/// <param name="ChunkIndex">This chunk's zero-based position in the staged body.</param>
/// <param name="ChunkCount">The total number of chunks the body was split into (≥ 1).</param>
/// <param name="Chunk">This chunk's slice of the reassembled Base64 body (see <see cref="ServerConfigRawEditCodec"/>).</param>
public sealed record ServerConfigRawEditChunk(
    string CorrelationId,
    int ChunkIndex,
    int ChunkCount,
    string Chunk);
