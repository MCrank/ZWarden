namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// Lift a ban on a Server by account username (F19). The target Server rides the envelope's
/// <see cref="Envelope{TPayload}.ServerId"/>; the <see cref="Username"/> is quoted into PZ's
/// <c>unbanuser "&lt;user&gt;"</c> (mandatory quoting — research §7 quirk 10). It is a <b>non-mutating</b>,
/// server-scoped Operation (ADR 0026), so it does not claim the per-server lock (ADR 0022). The Agent validates
/// and quotes the argument and reports <see cref="OperationCompleted"/> with a <see cref="PlayerActionResult"/>;
/// the control plane lifts the ban in its registry from that terminal result (ADR 0027).
/// </summary>
/// <param name="Username">The PZ account username to unban. Validated Agent-side (and at the Web edge) before
/// it is quoted into the command.</param>
[ProtocolMessage("players.unban")]
public sealed record UnbanPlayer(string Username) : AgentCommand;
