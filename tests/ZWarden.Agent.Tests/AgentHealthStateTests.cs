using ZWarden.Agent.Health;
using ZWarden.Contracts.Protocol;

namespace ZWarden.Agent.Tests;

/// <summary>
/// F8 test plan item 5: the in-process health-state model over F7's <see cref="AgentHealthStatus"/>
/// is readable, settable, and logs transitions.
/// </summary>
public class AgentHealthStateTests
{
    private static AgentHealthState New(out RecordingLogger<AgentHealthState> logger)
    {
        logger = new RecordingLogger<AgentHealthState>();
        return new AgentHealthState(logger);
    }

    [Test]
    public async Task Default_is_healthy()
    {
        var state = New(out _);

        await Assert.That(state.Current).IsEqualTo(AgentHealthStatus.Healthy);
    }

    [Test]
    public async Task Report_updates_current_and_reason()
    {
        var state = New(out _);

        state.Report(AgentHealthStatus.Degraded, "control-plane unreachable");

        await Assert.That(state.Current).IsEqualTo(AgentHealthStatus.Degraded);
        await Assert.That(state.Reason).IsEqualTo("control-plane unreachable");
    }

    [Test]
    public async Task A_transition_is_logged_with_its_reason()
    {
        var state = New(out var logger);

        state.Report(AgentHealthStatus.Unhealthy, "identity load failed");

        bool logged = logger.Entries.Any(e =>
            e.Message.Contains("health changed", StringComparison.OrdinalIgnoreCase)
            && e.Message.Contains("identity load failed", StringComparison.Ordinal));
        await Assert.That(logged).IsTrue();
    }

    [Test]
    public async Task Reporting_the_same_status_does_not_log_a_transition()
    {
        var state = New(out var logger);

        // Default is Healthy; reporting Healthy again is not a transition.
        state.Report(AgentHealthStatus.Healthy, "still fine");

        await Assert.That(logger.Entries).IsEmpty();
    }
}
