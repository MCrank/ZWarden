namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// Delete a registered Server's canonical container (#271). The target Server is the envelope's
/// <see cref="Envelope{TPayload}.ServerId"/>. If the container is running the Agent warns players (the optional
/// <see cref="Plan"/>, as <see cref="RestartServer"/>) and stops it safely; it then removes the container (owned,
/// stopped, by its ServerId name — ADR 0045). World data and backups are <b>never</b> touched. An absent container is
/// already deleted, so it succeeds. It is a <b>mutating, server-scoped</b> Operation, so it claims the per-server lock
/// (ADR 0022); on success the control plane removes the Server from the fleet.
/// </summary>
/// <param name="Plan">The optional graceful-warning plan before the safe stop. <c>null</c> ⇒ the Agent's default
/// schedule; an empty schedule skips the warning. Ignored when the server is not running.</param>
[ProtocolMessage("provisioning.delete-server")]
public sealed record DeleteServer(GracefulRestartPlan? Plan = null) : AgentCommand;
