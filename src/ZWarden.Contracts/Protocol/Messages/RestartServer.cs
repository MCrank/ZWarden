namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// Restart a registered Server's canonical container safely (F15). The target Server is the envelope's
/// <see cref="Envelope{TPayload}.ServerId"/>. The Agent resolves the owned container from the ServerId and issues
/// a Docker restart with the same safe stop timeout as <see cref="StopServer"/>, so the restart's stop half
/// triggers the image's console <c>save</c>→<c>quit</c> shutdown before the container comes back up. It is a
/// <b>mutating, server-scoped</b> Operation, so it claims the per-server lock (ADR 0022), and acts only on a
/// container the Agent owns (F13, trust-boundaries §4).
/// <para>
/// The optional <see cref="Plan"/> makes the restart <b>graceful</b> (#114): before the stop, the Agent broadcasts
/// a <c>servermsg</c> countdown to connected players over its owned RCON connection (ADR 0026). It is additive
/// (ADR 0020 — the protocol version does not bump): a <c>null</c> plan means the Agent applies its configured
/// default schedule, and an empty schedule means restart immediately without warning.
/// </para>
/// </summary>
/// <param name="Plan">The optional graceful-restart broadcast plan. <c>null</c> ⇒ the Agent's default schedule.</param>
/// <param name="BackupFirst">#379: take an automatic (<c>PreOperation</c>) backup of the world before the countdown
/// starts — set only for the restart that applies mod updates (#273), never a plain restart. A failed backup fails the
/// Operation and the server is not restarted. Additive (ADR 0020): <c>false</c> when absent.</param>
[ProtocolMessage("lifecycle.restart-server")]
public sealed record RestartServer(GracefulRestartPlan? Plan = null, bool BackupFirst = false) : AgentCommand;
