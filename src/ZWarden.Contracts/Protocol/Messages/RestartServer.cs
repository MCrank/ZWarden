namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// Restart a registered Server's canonical container safely (F15). The target Server is the envelope's
/// <see cref="Envelope{TPayload}.ServerId"/>, so — like <see cref="CreateServer"/> — this command carries
/// <b>no payload</b>. The Agent resolves the owned container from the ServerId and issues a Docker restart
/// with the same safe stop timeout as <see cref="StopServer"/>, so the restart's stop half triggers the
/// image's console <c>save</c>→<c>quit</c> shutdown before the container comes back up. It is a
/// <b>mutating, server-scoped</b> Operation, so it claims the per-server lock (ADR 0022), and acts only on a
/// container the Agent owns (F13, trust-boundaries §4).
/// </summary>
[ProtocolMessage("lifecycle.restart-server")]
public sealed record RestartServer : AgentCommand;
