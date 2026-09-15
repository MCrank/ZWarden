namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// Run one operator-authored RCON command line against a Server from the remote administrative console (F28). The
/// target Server rides the envelope's <see cref="Envelope{TPayload}.ServerId"/>; the <see cref="Input"/> is the
/// single line the Agent sends over the Agent-owned RCON connection (ADR 0026 — the Agent sends it as-is; quoting
/// is the operator's job, research §7 quirk 10). It is a <b>non-mutating</b>, server-scoped Operation (an RCON
/// passthrough already serialized by the Agent's single-socket gate — ADR 0026), so it does not claim the
/// per-server lock (ADR 0022), and it is gated by the elevated <c>Console.Execute</c> permission.
/// <para>
/// The console runs arbitrary RCON — the server console as <c>admin</c> (research §7) — but <b>not</b> a shell
/// (PRD 18). The input is validated and policy-checked by <c>ConsoleCommandRules</c> on both the Web edge and the
/// Agent (ADR 0032): a single, bounded, printable-ASCII line, and never a credential-minting/exposing command or
/// <c>quit</c>. The reply is reported on <see cref="OperationCompleted"/> as an untrusted, bounded
/// <see cref="ConsoleCommandResult"/> (trust-boundaries.md §8), carried verbatim for escaping at render.
/// </para>
/// </summary>
/// <param name="Input">The RCON command line to run. Named <c>Input</c> — not <c>Command</c>/<c>Cmd</c>/… — because
/// it is a policy-gated RCON line, not a free-form shell string (the closed-vocabulary canary, trust-boundaries.md
/// §9 rule 3). Validated (single line, printable ASCII, bounded) and policy-checked before it is sent.</param>
[ProtocolMessage("console.execute")]
public sealed record ExecuteConsoleCommand(string Input) : AgentCommand;
