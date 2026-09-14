namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// Remove a user from a Server's whitelist by account username (F19). The target Server rides the envelope's
/// <see cref="Envelope{TPayload}.ServerId"/>; the <see cref="Username"/> is quoted into PZ's
/// <c>removeuserfromwhitelist "&lt;user&gt;"</c> (mandatory quoting — research §7 quirk 10). Whitelist
/// <i>removal</i> is supported; whitelist <i>addition</i> is not (ADR 0012). It is a <b>non-mutating</b>,
/// server-scoped Operation (ADR 0026), so it does not claim the per-server lock (ADR 0022). The Agent validates
/// and quotes the argument and reports <see cref="OperationCompleted"/> with a <see cref="PlayerActionResult"/>.
/// </summary>
/// <param name="Username">The PZ account username to remove from the whitelist. Validated Agent-side (and at
/// the Web edge) before it is quoted into the command.</param>
[ProtocolMessage("players.remove-from-whitelist")]
public sealed record RemoveFromWhitelist(string Username) : AgentCommand;
