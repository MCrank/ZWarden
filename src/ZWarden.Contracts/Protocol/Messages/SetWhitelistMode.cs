namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// Toggle a Server's whitelist <b>mode</b> — the <c>Open</c> server option (F19). The target Server rides the
/// envelope's <see cref="Envelope{TPayload}.ServerId"/>; the Agent runs PZ's <c>changeoption Open
/// &lt;true|false&gt;</c> over RCON, which persists the key to the server's INI. <see cref="Open"/> is
/// <c>true</c> to let clients join without a whitelist account (PZ's default) and <c>false</c> to close the
/// server to non-whitelisted players. It is a <b>non-mutating</b>, server-scoped Operation in the F11 sense (an
/// RCON passthrough — ADR 0026), so it does not claim the per-server lock (ADR 0022); reconciling the INI write
/// against configuration revisions is F20b's concern, not this command's. The Agent reports
/// <see cref="OperationCompleted"/> with a <see cref="PlayerActionResult"/> confirming PZ's echoed
/// <c>Option : Open is now : &lt;value&gt;</c>.
/// </summary>
/// <param name="Open"><c>true</c> to open the server to non-whitelisted players; <c>false</c> to close it.</param>
[ProtocolMessage("players.set-whitelist-mode")]
public sealed record SetWhitelistMode(bool Open) : AgentCommand;
