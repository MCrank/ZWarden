namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// The optional graceful-restart plan carried by a <see cref="RestartServer"/> command (#114): warn connected
/// players with a <c>servermsg</c> broadcast at each of a descending set of lead-times before the server stops,
/// then proceed with the F15 safe stop→start. It is a plain, additive parameter on the command (ADR 0020 — the
/// protocol version does not bump), so an older Agent or a caller that omits it simply restarts without warning.
/// </summary>
/// <param name="WarningLeadSeconds">The seconds-before-stop at which to broadcast, strictly descending (e.g.
/// <c>[300, 60, 30, 10]</c>). An <b>empty</b> list means "skip the broadcast, restart immediately"; the schedule
/// is validated and bounded by <c>GracefulRestartRules</c> on both the Web edge and the Agent.</param>
/// <param name="Reason">An optional short, printable-ASCII clause appended to each countdown notice (e.g.
/// "Applying mod changes."). Validated by <c>GracefulRestartRules</c>.</param>
public sealed record GracefulRestartPlan(IReadOnlyList<int> WarningLeadSeconds, string? Reason = null);
