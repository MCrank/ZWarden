using ZWarden.Application.Diagnostics;
using ZWarden.Diagnostics.Evaluators;
using ZWarden.Domain.Ids;

namespace ZWarden.Diagnostics.Tests.Evaluators;

/// <summary>
/// F29 PR-A: the pure agent-connectivity rollup. Only enabled Agents are expected online; a disconnected Agent
/// seen within the window is a transient Warn, one stale/never-seen is a Fail, and the worst case wins. No
/// registry or repository here.
/// </summary>
public class AgentConnectivityDiagnosticTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private static AgentPresenceFacts Agent(bool enabled, bool connected, DateTimeOffset? lastSeen) =>
        new(AgentId.New(), Label: null, IsEnabled: enabled, IsConnected: connected, LastSeenAt: lastSeen);

    [Test]
    public async Task No_enabled_agents_is_skipped()
    {
        DiagnosticCheck check = AgentConnectivityDiagnostic.Evaluate(
            [Agent(enabled: false, connected: false, lastSeen: null)], Now);

        await Assert.That(check.Domain).IsEqualTo(DiagnosticDomain.Agent);
        await Assert.That(check.Status).IsEqualTo(DiagnosticStatus.Skipped);
    }

    [Test]
    public async Task An_empty_fleet_is_skipped()
    {
        DiagnosticCheck check = AgentConnectivityDiagnostic.Evaluate([], Now);

        await Assert.That(check.Status).IsEqualTo(DiagnosticStatus.Skipped);
    }

    [Test]
    public async Task All_connected_agents_pass()
    {
        DiagnosticCheck check = AgentConnectivityDiagnostic.Evaluate(
            [Agent(true, connected: true, Now), Agent(true, connected: true, Now)], Now);

        await Assert.That(check.Status).IsEqualTo(DiagnosticStatus.Pass);
        await Assert.That(check.Summary).Contains("2");
    }

    [Test]
    public async Task A_disconnected_agent_seen_recently_warns()
    {
        DiagnosticCheck check = AgentConnectivityDiagnostic.Evaluate(
            [Agent(true, connected: false, Now.AddMinutes(-1))], Now, TimeSpan.FromMinutes(5));

        await Assert.That(check.Status).IsEqualTo(DiagnosticStatus.Warn);
    }

    [Test]
    public async Task A_disconnected_agent_stale_beyond_the_window_fails()
    {
        DiagnosticCheck check = AgentConnectivityDiagnostic.Evaluate(
            [Agent(true, connected: false, Now.AddMinutes(-30))], Now, TimeSpan.FromMinutes(5));

        await Assert.That(check.Status).IsEqualTo(DiagnosticStatus.Fail);
    }

    [Test]
    public async Task A_disconnected_agent_never_seen_fails()
    {
        DiagnosticCheck check = AgentConnectivityDiagnostic.Evaluate(
            [Agent(true, connected: false, lastSeen: null)], Now);

        await Assert.That(check.Status).IsEqualTo(DiagnosticStatus.Fail);
    }

    [Test]
    public async Task An_offline_agent_outweighs_a_reconnecting_one()
    {
        DiagnosticCheck check = AgentConnectivityDiagnostic.Evaluate(
            [
                Agent(true, connected: true, Now),
                Agent(true, connected: false, Now.AddMinutes(-1)),
                Agent(true, connected: false, lastSeen: null),
            ],
            Now,
            TimeSpan.FromMinutes(5));

        await Assert.That(check.Status).IsEqualTo(DiagnosticStatus.Fail);
        await Assert.That(check.Detail).Contains("offline");
    }

    [Test]
    public async Task Disabled_agents_are_ignored_in_the_rollup()
    {
        DiagnosticCheck check = AgentConnectivityDiagnostic.Evaluate(
            [Agent(true, connected: true, Now), Agent(enabled: false, connected: false, lastSeen: null)], Now);

        await Assert.That(check.Status).IsEqualTo(DiagnosticStatus.Pass);
        await Assert.That(check.Summary).Contains("1");
    }
}
