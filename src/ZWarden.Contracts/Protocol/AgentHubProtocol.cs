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
}
