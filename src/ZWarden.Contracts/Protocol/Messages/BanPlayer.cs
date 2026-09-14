namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// Ban a player from a Server by account username (F19). The target Server rides the envelope's
/// <see cref="Envelope{TPayload}.ServerId"/>; the <see cref="Username"/> (and optional <see cref="Reason"/>)
/// are the payload the Agent quotes into PZ's <c>banuser "&lt;user&gt;" -r "&lt;reason&gt;"</c> (mandatory
/// quoting — research §7 quirk 10). Account-username bans only — Steam-ID (<c>banid</c>) and IP (<c>banip</c>)
/// bans are out of scope (ADR 0027). It is a <b>non-mutating</b>, server-scoped Operation (ADR 0026), so it
/// does not claim the per-server lock (ADR 0022). The Agent validates and quotes the arguments — no free-form
/// command crosses the boundary — and reports <see cref="OperationCompleted"/> with a
/// <see cref="PlayerActionResult"/>; the control plane records the ban in its registry from that terminal
/// result, never inferred from dispatch (trust-boundaries.md §3, ADR 0027).
/// </summary>
/// <param name="Username">The PZ account username to ban. Validated Agent-side (and at the Web edge) before it
/// is quoted into the command.</param>
/// <param name="Reason">An optional operator-facing ban reason, quoted into <c>-r</c>; validated (bounded, no
/// quotes or control characters). <c>null</c> to omit the reason.</param>
[ProtocolMessage("players.ban")]
public sealed record BanPlayer(string Username, string? Reason = null) : AgentCommand;
