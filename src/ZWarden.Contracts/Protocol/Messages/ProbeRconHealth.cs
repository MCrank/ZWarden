namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// A non-mutating, <b>per-server</b> probe of a Server's RCON reachability (F18). Like
/// <see cref="ProbeDockerHealth"/> it carries no payload — the target Server rides the envelope's
/// <c>ServerId</c> and the operation is its <c>OperationId</c> — but, unlike the host-level Docker
/// probe, it names a Server because RCON is per-container (private on the ZWarden network, port
/// 27015, never host-published — trust-boundaries.md §5, PRD 29). The Agent opens its
/// Agent-owned RCON connection to that Server's container, authenticates, and reports
/// <see cref="OperationCompleted"/> with a <see cref="RconHealthResult"/>: succeeded when RCON is
/// reachable and authenticated, failed with an Agent-authored reason (RCON disabled, unreachable,
/// the password rejected, or a timeout) otherwise. Read-only, so it never claims the per-server lock
/// (ADR 0022). It carries no free-form command — the admin command surface is F19/F28.
/// </summary>
[ProtocolMessage("diagnostics.rcon-health")]
public sealed record ProbeRconHealth : AgentCommand;
