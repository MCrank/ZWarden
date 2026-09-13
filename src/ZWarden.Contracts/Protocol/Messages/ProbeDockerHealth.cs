namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// A non-mutating, host-level probe of the Agent's Docker connectivity (F13). Like <see cref="PingAgent"/> it
/// carries no payload — the operation is the envelope's <c>OperationId</c> — and, being non-mutating and
/// host-level, never claims a per-server lock (ADR 0022: read-only and host-level Operations never contend).
/// The Agent runs its Docker health probe (daemon reachability and the API version negotiated over
/// <c>/_ping</c>) and reports <see cref="OperationCompleted"/> on the same operation: succeeded when the
/// daemon is reachable, failed with an Agent-authored reason otherwise. It is the F13 end-to-end slice, the
/// second leaf of the closed <see cref="AgentCommand"/> vocabulary; it carries no free-form command.
/// </summary>
[ProtocolMessage("diagnostics.docker-health")]
public sealed record ProbeDockerHealth : AgentCommand;
