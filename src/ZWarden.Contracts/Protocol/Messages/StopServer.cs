namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// Stop a registered Server's canonical container safely (F15). The target Server is the envelope's
/// <see cref="Envelope{TPayload}.ServerId"/>, so — like <see cref="CreateServer"/> — this command carries
/// <b>no payload</b>. The Agent resolves the owned container from the ServerId and issues a Docker stop with a
/// timeout longer than the container's in-image save grace, so the image's entrypoint converts the SIGTERM
/// into the blessed console <c>save</c>→<c>quit</c> over the stdin FIFO before any SIGKILL — <b>never a bare
/// SIGTERM to the JVM</b>, which the PZ developers discourage and which truncates the world save. It is a
/// <b>mutating, server-scoped</b> Operation, so it claims the per-server lock (ADR 0022), and acts only on a
/// container the Agent owns (F13, trust-boundaries §4).
/// </summary>
[ProtocolMessage("lifecycle.stop-server")]
public sealed record StopServer : AgentCommand;
