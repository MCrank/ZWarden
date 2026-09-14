using ZWarden.Domain.Ids;

namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// A batch of a Server's log lines the Agent has streamed upward while an operator is watching (F27) — the
/// realisation of the <c>LogEntry → F27</c> slot reserved on <see cref="AgentEvent"/>. Streaming is on-demand:
/// the Agent follows a Server's container logs only while a live subscription is open, batching lines to avoid a
/// per-line message storm. Logs are <b>untrusted input</b> (PRD 38, trust-boundaries.md §8) — every line is
/// sanitized on the Agent before it is put on the wire and rendered as data by ZWarden.Web. Like metrics, log
/// lines are high-churn and transient: Web holds only a bounded per-Server tail in memory and never persists
/// them. Observed data (trust §3); a batch for a Server the reporting Agent does not own is ignored on ingest.
/// </summary>
/// <param name="ServerId">The Server whose logs these lines are from (matched to a persisted Server on ingest).</param>
/// <param name="Lines">The sanitized lines, in the order the Agent read them.</param>
/// <param name="Dropped"><see langword="true"/> when the Agent's rate cap coalesced lines away since the last
/// batch — the loss is surfaced to the operator rather than silently swallowed, and the socket is protected from
/// a flooding server.</param>
[ProtocolMessage("server.log-batch")]
public sealed record ServerLogBatch(ServerId ServerId, IReadOnlyList<ServerLogLine> Lines, bool Dropped) : AgentEvent;

/// <summary>
/// One sanitized log line within a <see cref="ServerLogBatch"/> (F27). <see cref="Sequence"/> is a per-Server
/// monotonic cursor assigned by the Agent, so a live viewer can request only lines after the last it has seen and
/// ordering survives batching. <see cref="Text"/> has been stripped of ANSI/control sequences and length-capped
/// on the Agent (PRD 38); it is rendered as data, never markup.
/// </summary>
/// <param name="Sequence">A per-Server monotonic line number the Agent assigns (the tail cursor).</param>
/// <param name="Timestamp">When the line was written, as reported by the container (UTC), or the Agent's read
/// time when the container did not stamp it.</param>
/// <param name="Stream">Which standard stream the line came from — stdout or stderr.</param>
/// <param name="Text">The sanitized line text, newline stripped.</param>
/// <param name="Truncated"><see langword="true"/> when the line exceeded the Agent's length cap and was cut.</param>
public sealed record ServerLogLine(
    long Sequence,
    DateTimeOffset Timestamp,
    LogStreamKind Stream,
    string Text,
    bool Truncated);
