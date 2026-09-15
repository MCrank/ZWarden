using ZWarden.Application.Diagnostics;

namespace ZWarden.Diagnostics.Evaluators;

/// <summary>
/// Rolls the enrolled Agents' <see cref="AgentPresenceFacts"/> up into one
/// <see cref="DiagnosticDomain.Agent"/> check (F29). A <b>pure</b> function of the facts, a clock, and a
/// staleness window — no registry, no repository — so the rollup is unit-tested. Only <b>enabled</b> Agents are
/// expected online: with none enabled the check is <see cref="DiagnosticStatus.Skipped"/>; a disconnected Agent
/// seen within the window is a <see cref="DiagnosticStatus.Warn"/> (a transient reconnect); one never seen or
/// stale beyond the window is a <see cref="DiagnosticStatus.Fail"/>. The check reports the worst case with counts.
/// </summary>
public static class AgentConnectivityDiagnostic
{
    /// <summary>The default window after an Agent's last-seen beyond which a disconnected Agent is a failure.</summary>
    public static readonly TimeSpan DefaultStaleWindow = TimeSpan.FromMinutes(5);

    /// <summary>Evaluates the enrolled Agents' presence into a single <see cref="DiagnosticDomain.Agent"/> check
    /// as of <paramref name="now"/>.</summary>
    public static DiagnosticCheck Evaluate(
        IReadOnlyList<AgentPresenceFacts> agents, DateTimeOffset now, TimeSpan? staleWindow = null)
    {
        ArgumentNullException.ThrowIfNull(agents);
        TimeSpan window = staleWindow ?? DefaultStaleWindow;

        List<AgentPresenceFacts> expected = agents.Where(a => a.IsEnabled).ToList();
        if (expected.Count == 0)
        {
            return Check(DiagnosticStatus.Skipped, "No enabled Agents are enrolled.");
        }

        int connected = 0;
        int reconnecting = 0;
        int offline = 0;
        foreach (AgentPresenceFacts agent in expected)
        {
            if (agent.IsConnected)
            {
                connected++;
            }
            else if (agent.LastSeenAt is { } seen && now - seen <= window)
            {
                reconnecting++;
            }
            else
            {
                offline++;
            }
        }

        int total = expected.Count;
        if (offline > 0)
        {
            return Check(
                DiagnosticStatus.Fail,
                $"{offline} of {total} Agent(s) are offline.",
                $"{connected} connected, {reconnecting} reconnecting, {offline} offline.");
        }

        if (reconnecting > 0)
        {
            return Check(
                DiagnosticStatus.Warn,
                $"{reconnecting} of {total} Agent(s) are not currently connected.",
                $"{connected} connected, {reconnecting} reconnecting.");
        }

        return Check(DiagnosticStatus.Pass, $"All {total} Agent(s) are connected.");
    }

    private static DiagnosticCheck Check(DiagnosticStatus status, string summary, string? detail = null) =>
        DiagnosticCheck.Create(DiagnosticDomain.Agent, status, summary, detail);
}
