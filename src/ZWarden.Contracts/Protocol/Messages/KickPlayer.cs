namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// Kick a connected player from a Server by account username (F19). The target Server rides the envelope's
/// <see cref="Envelope{TPayload}.ServerId"/>; the <see cref="Username"/> (and optional <see cref="Reason"/>)
/// are the payload the Agent quotes into PZ's <c>kickuser "&lt;user&gt;" -r "&lt;reason&gt;"</c> (mandatory
/// quoting — research §7 quirk 10). It is a <b>non-mutating</b>, server-scoped Operation (an RCON passthrough
/// already serialized by the Agent's single-socket gate — ADR 0026), so it does not claim the per-server lock
/// (ADR 0022). The Agent validates and quotes the arguments — no free-form command crosses the boundary
/// (trust-boundaries.md §9 rule 3) — and reports <see cref="OperationCompleted"/> with a
/// <see cref="PlayerActionResult"/> parsed from PZ's stable kick strings.
/// </summary>
/// <param name="Username">The PZ account username to kick. Validated Agent-side (and at the Web edge) — no
/// quotes, whitespace, control characters, or leading dash — before it is quoted into the command.</param>
/// <param name="Reason">An optional operator-facing kick reason, quoted into <c>-r</c>; validated (bounded, no
/// quotes or control characters). <c>null</c> to omit the reason.</param>
[ProtocolMessage("players.kick")]
public sealed record KickPlayer(string Username, string? Reason = null) : AgentCommand;
