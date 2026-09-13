namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// Start a registered Server's canonical container (F15). The target Server is the envelope's
/// <see cref="Envelope{TPayload}.ServerId"/>, so — like <see cref="CreateServer"/> — this command carries
/// <b>no payload</b>: the Agent resolves the owned container from the ServerId and issues the start verb. It
/// is a <b>mutating, server-scoped</b> Operation, so it claims the per-server lock (ADR 0022). The Agent acts
/// only on a container it owns (F13 ownership enforcement, trust-boundaries §4) and carries no free-form
/// command.
/// </summary>
[ProtocolMessage("lifecycle.start-server")]
public sealed record StartServer : AgentCommand;
