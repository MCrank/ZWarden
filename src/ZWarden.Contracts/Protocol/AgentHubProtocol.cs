namespace ZWarden.Contracts.Protocol;

/// <summary>
/// The transport-level names that pin the SignalR Agent hub (F10): the endpoint path and the hub method
/// names the Agent invokes on ZWarden.Web. Both components reference <c>ZWarden.Contracts</c>, so sharing the
/// strings here keeps the server hub and the Agent client in lock-step — a rename is a compile-time change on
/// both sides rather than a silent wire mismatch. This is transport plumbing, <b>not</b> a protocol message:
/// it carries no <see cref="ProtocolMessageAttribute"/> and is neither an <c>AgentCommand</c> nor an
/// <c>AgentEvent</c>, so the closed-vocabulary guard (ADR 0020) is unaffected. Messages still travel as
/// <see cref="Envelope{TPayload}"/> arguments serialized with <see cref="ProtocolJson.Options"/> (wired onto
/// SignalR's JSON protocol by each side).
/// </summary>
public static class AgentHubProtocol
{
    /// <summary>The path ZWarden.Web maps the Agent hub at and the Agent connects to (over WSS).</summary>
    public const string Path = "/agent/hub";

    /// <summary>
    /// The Agent's opening call: it sends its <c>Envelope&lt;AgentHello&gt;</c> and receives a
    /// <see cref="ProtocolNegotiationResult"/>. On an incompatible result the server aborts the connection
    /// (ADR 0020); negotiation is this first exchange, not the handshake.
    /// </summary>
    public const string Hello = "Hello";

    /// <summary>The Agent's periodic liveness call, carrying an <c>Envelope&lt;AgentHeartbeat&gt;</c>.</summary>
    public const string Heartbeat = "Heartbeat";

    /// <summary>The Agent's post-(re)connect state report, carrying an <c>Envelope&lt;AgentStateSnapshot&gt;</c>.</summary>
    public const string StateSnapshot = "StateSnapshot";

    /// <summary>The Agent's incremental run-state transition report, carrying an
    /// <c>Envelope&lt;ServerStateChanged&gt;</c> (F16); the Server is the envelope's <c>ServerId</c>.</summary>
    public const string ServerStateChanged = "ServerStateChanged";

    /// <summary>The Agent's incremental health transition report, carrying an
    /// <c>Envelope&lt;HealthChanged&gt;</c> (F16); the Server is the envelope's <c>ServerId</c>.</summary>
    public const string HealthChanged = "HealthChanged";

    /// <summary>The Agent's periodic runtime-metrics report, carrying an <c>Envelope&lt;ServerMetricsReport&gt;</c>
    /// (F16); ZWarden.Web keeps only the latest per Server and pushes it to the live UI.</summary>
    public const string MetricsReport = "MetricsReport";

    /// <summary>
    /// The client method ZWarden.Web invokes on the Agent to dispatch an operation's command (F11). Its one
    /// argument is the <b>canonical wire JSON string</b> of an <c>Envelope&lt;AgentCommand&gt;</c> (produced by
    /// <see cref="ProtocolJson.Serialize{TPayload}"/>), so the single channel carries every command in the
    /// closed vocabulary and the Agent dispatches on the envelope's <c>messageType</c> discriminator — no
    /// per-command hub method, and no polymorphic-payload configuration on SignalR's own JSON protocol.
    /// </summary>
    public const string ReceiveCommand = "ReceiveCommand";

    /// <summary>The Agent's progress report for an in-flight operation, carrying an
    /// <c>Envelope&lt;OperationProgress&gt;</c> (F11); the operation is the envelope's <c>OperationId</c>.</summary>
    public const string OperationProgress = "OperationProgress";

    /// <summary>The Agent's terminal report for an operation, carrying an
    /// <c>Envelope&lt;OperationCompleted&gt;</c> (F11); the operation is the envelope's <c>OperationId</c>.</summary>
    public const string OperationCompleted = "OperationCompleted";

    /// <summary>The Agent's live log push while a subscription is open, carrying an
    /// <c>Envelope&lt;ServerLogBatch&gt;</c> (F27); the Server is the batch's <c>ServerId</c>.</summary>
    public const string ServerLogBatch = "ServerLogBatch";

    /// <summary>
    /// The client method ZWarden.Web invokes on the Agent to <b>begin</b> following a Server's logs (F27), with
    /// the Server's canonical id string as its one argument. A live-log subscription is deliberately <b>not</b> an
    /// Operation (ADR 0022 — no per-server lock, no audit, no lifecycle row) and <b>not</b> an <c>AgentCommand</c>
    /// (that is the operation-dispatch vocabulary): it is ephemeral, read-only, per-viewer transport control, so it
    /// rides its own channel rather than <see cref="ReceiveCommand"/> (ADR 0030). Web ref-counts viewers and calls
    /// this only for the first watcher of a Server.
    /// </summary>
    public const string StartServerLogStream = "StartServerLogStream";

    /// <summary>The client method ZWarden.Web invokes on the Agent to <b>stop</b> following a Server's logs (F27),
    /// with the Server's canonical id string as its one argument — called when the last viewer leaves (ADR 0030).</summary>
    public const string StopServerLogStream = "StopServerLogStream";

    /// <summary>
    /// The client method ZWarden.Web invokes on the Agent to request a <b>live, non-mutating read</b> of one of a
    /// Server's configuration files (F20c). Its three arguments are the Server's canonical id string, the
    /// <c>PzConfigFile</c> name, and an opaque <b>correlation id</b> the Agent echoes on every reply chunk. Like the
    /// log-stream control this is deliberately <b>transport plumbing</b>, not a protocol message: it is neither an
    /// <c>AgentCommand</c> (the operation-dispatch vocabulary — a read takes no lock, writes no audit row, records no
    /// revision, ADR 0022/0011) nor an <c>AgentEvent</c>, so the closed-vocabulary guard (ADR 0020) is unaffected. The
    /// Agent replies with one or more <see cref="ServerConfigContent"/> sends (ADR 0041).
    /// </summary>
    public const string RequestServerConfigRead = "RequestServerConfigRead";

    /// <summary>
    /// The Agent's reply to <see cref="RequestServerConfigRead"/> (F20c), carrying an
    /// <c>Envelope&lt;ServerConfigContent&gt;</c> — one sequenced chunk of the canonical-JSON view of the parsed file
    /// (the current values, harvested comments, raw text, diagnostics, and drift baseline hash). A parsed file can
    /// exceed SignalR's per-message ceiling, so the reply is chunked and reassembled by correlation id on
    /// ZWarden.Web, bounded and transient (ADR 0041). The Server is the envelope's <c>ServerId</c>.
    /// </summary>
    public const string ServerConfigContent = "ServerConfigContent";

    /// <summary>
    /// The client method ZWarden.Web invokes on the Agent to <b>stage</b> one chunk of an operator-authored
    /// whole-file configuration edit before enqueuing the <c>ConfigApplyRaw</c> Operation that applies it (F20c
    /// PR-D, ADR 0042). Its four bare arguments are a <see cref="ServerConfigRawEditChunk"/>'s correlation id, chunk
    /// index, chunk count, and Base64 slice — the reverse-direction sibling of <see cref="RequestServerConfigRead"/>.
    /// Like the read request this is deliberately <b>transport plumbing</b>, not a protocol message: it is neither an
    /// <c>AgentCommand</c> nor an <c>AgentEvent</c>, so the closed-vocabulary guard (ADR 0020) is unaffected and the
    /// arbitrary file text never rides a command. The Agent reassembles the chunks by correlation id into a bounded,
    /// transient buffer and applies them when the matching Operation runs.
    /// </summary>
    public const string StageServerConfigRawEdit = "StageServerConfigRawEdit";
}
