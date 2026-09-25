namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// Recreate a registered Server's canonical container, preserving its data (#229, ADR 0045). The target Server is
/// the envelope's <see cref="Envelope{TPayload}.ServerId"/>. If the container is running the Agent warns players
/// (the optional <see cref="Plan"/>, as <see cref="RestartServer"/>) and stops it safely; it then removes the
/// container (owned, stopped, by its ServerId name), creates a new one from the <b>same closed template</b> — the
/// same ServerId-derived <c>/pz/data</c> and <c>/pz/server</c> binds, so the world, config and installed PZ build
/// survive with no re-download — and starts it again only if it was running before. If the new container cannot
/// be created or started, the Agent rolls back to the previous ports and run state and reports the failure. An
/// absent container is recreated from scratch (repair). It is a <b>mutating, server-scoped</b> Operation, so it
/// claims the per-server lock (ADR 0022); the completion carries the resulting ports and container id in
/// <see cref="OperationCompleted.Provision"/>.
/// </summary>
/// <param name="GamePort">The new host game port; the pair is this port and the one above it. <c>null</c> ⇒ keep
/// the container's current pair (or allocate the next free stride if it has none).</param>
/// <param name="Plan">The optional graceful-warning plan before the safe stop. <c>null</c> ⇒ the Agent's default
/// schedule; an empty schedule skips the warning. Ignored when the server is not running.</param>
[ProtocolMessage("provisioning.recreate-server")]
public sealed record RecreateServer(int? GamePort = null, GracefulRestartPlan? Plan = null) : AgentCommand;
