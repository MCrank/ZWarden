namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// Enumerate the players currently connected to a Server (F19). Like <see cref="ProbeRconHealth"/> this is a
/// non-mutating, <b>per-server</b> command carrying <b>no payload</b> — the target Server rides the envelope's
/// <see cref="Envelope{TPayload}.ServerId"/> and the operation is its <c>OperationId</c>. The Agent runs PZ's
/// <c>players</c> command over its Agent-owned RCON connection (excluded from the server's RCON debug log, so it
/// is safe to poll — research §7 quirk 13) and reports <see cref="OperationCompleted"/> with a
/// <see cref="PlayerRosterResult"/>. There are no connect/disconnect <i>events</i> in the shipped build
/// (<c>connections</c>/<c>disconnect</c> are disabled — ADR 0012), so enumeration is this on-demand poll, not a
/// stream. The roster is <b>untrusted</b> PZ output (trust-boundaries.md §8), carried verbatim for escaping at
/// render (F28); it carries no free-form command.
/// </summary>
[ProtocolMessage("players.list")]
public sealed record ListPlayers : AgentCommand;
