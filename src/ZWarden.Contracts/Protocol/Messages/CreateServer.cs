namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// Provision the canonical ZWarden.PZServer container for a registered Server (F14 PR-B). The target Server
/// is the envelope's <see cref="Envelope{TPayload}.ServerId"/>. The Agent derives everything else (the pinned
/// image, the ZWarden network, the data-mount root, the memory limit) from its own configuration. The host UDP
/// pair is the operator's <see cref="GamePort"/> when given (#229), otherwise the Agent allocates the next free
/// two-port stride itself from the host's live bindings (F13). It is a <b>mutating, server-scoped</b> Operation,
/// so it claims the per-server lock (ADR 0022). The Agent builds the container from F13's closed create-template
/// (never from caller input — trust-boundaries §4/§5.3), starts it, and reports <see cref="OperationCompleted"/>
/// carrying the ports and container id in its <see cref="OperationCompleted.Provision"/> result. It carries no
/// free-form command.
/// </summary>
/// <param name="GamePort">The operator-chosen host game port (#229); the pair is this port and the one above it.
/// <c>null</c> ⇒ the Agent allocates the next free stride. Additive (ADR 0020): an older caller omits it. The Agent
/// validates it (<c>HostPortRules</c>) and refuses a port already published on the host.</param>
[ProtocolMessage("provisioning.create-server")]
public sealed record CreateServer(int? GamePort = null) : AgentCommand;
