namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// Provision the canonical ZWarden.PZServer container for a registered Server (F14 PR-B). The target Server
/// is the envelope's <see cref="Envelope{TPayload}.ServerId"/>, so — like <see cref="PingAgent"/> and
/// <see cref="ProbeDockerHealth"/> — this command carries <b>no payload</b>: the Agent derives everything
/// else (the pinned image, the ZWarden network, the data-mount root, the memory limit) from its own
/// configuration, and allocates the two-UDP-port stride itself from the host's live bindings (F13). It is a
/// <b>mutating, server-scoped</b> Operation, so it claims the per-server lock (ADR 0022). The Agent builds
/// the container from F13's closed create-template (never from caller input — trust-boundaries §4/§5.3),
/// starts it, and reports <see cref="OperationCompleted"/> carrying the allocated ports and container id in
/// its <see cref="OperationCompleted.Provision"/> result. It carries no free-form command.
/// </summary>
[ProtocolMessage("provisioning.create-server")]
public sealed record CreateServer : AgentCommand;
