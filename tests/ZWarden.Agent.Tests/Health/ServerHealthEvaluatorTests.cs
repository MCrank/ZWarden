using ZWarden.Agent.Health;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;

namespace ZWarden.Agent.Tests.Health;

/// <summary>
/// F16 PR-A: the pure hierarchical health rollup. The evaluator is a total function of the four probe facts —
/// container / process / startup / network — so the whole truth table is asserted here with no Docker. It maps
/// facts onto the five operator states (stopped/starting/healthy/degraded/failed) and the coarse run-state, and
/// produces a breakdown that explains a degraded or failed verdict.
/// </summary>
public class ServerHealthEvaluatorTests
{
    private static HealthEvaluation Evaluate(
        string state, string? health = null, long exitCode = 0, bool oom = false, bool? portsReachable = null)
        => ServerHealthEvaluator.Evaluate(new HealthProbeFacts(state, health, exitCode, oom, portsReachable));

    [Test]
    public async Task A_running_container_that_passes_every_probe_is_healthy()
    {
        HealthEvaluation e = Evaluate("running", health: "healthy", portsReachable: true);

        await Assert.That(e.Health).IsEqualTo(ServerHealth.Healthy);
        await Assert.That(e.RunState).IsEqualTo(ServerRunState.Running);
        await Assert.That(e.Breakdown.Container.Status).IsEqualTo(ProbeStatus.Pass);
        await Assert.That(e.Breakdown.Process.Status).IsEqualTo(ProbeStatus.Pass);
        await Assert.That(e.Breakdown.Network.Status).IsEqualTo(ProbeStatus.Pass);
    }

    [Test]
    public async Task A_running_container_still_starting_is_starting()
    {
        HealthEvaluation e = Evaluate("running", health: "starting", portsReachable: false);

        // Startup precedence: a starting server is Starting even if the port probe has not yet passed.
        await Assert.That(e.Health).IsEqualTo(ServerHealth.Starting);
        await Assert.That(e.RunState).IsEqualTo(ServerRunState.Starting);
        await Assert.That(e.Breakdown.Startup.Status).IsEqualTo(ProbeStatus.Warn);
    }

    [Test]
    public async Task A_running_container_with_an_unhealthy_process_has_failed()
    {
        HealthEvaluation e = Evaluate("running", health: "unhealthy", portsReachable: true);

        await Assert.That(e.Health).IsEqualTo(ServerHealth.Failed);
        await Assert.That(e.RunState).IsEqualTo(ServerRunState.Running);
        await Assert.That(e.Breakdown.Process.Status).IsEqualTo(ProbeStatus.Fail);
        await Assert.That(e.Reason).Contains("unhealthy");
    }

    [Test]
    public async Task A_running_healthy_container_with_an_unreachable_port_is_degraded()
    {
        HealthEvaluation e = Evaluate("running", health: "healthy", portsReachable: false);

        await Assert.That(e.Health).IsEqualTo(ServerHealth.Degraded);
        await Assert.That(e.Breakdown.Network.Status).IsEqualTo(ProbeStatus.Fail);
        await Assert.That(e.Reason).Contains("port");
    }

    [Test]
    public async Task A_running_container_with_no_healthcheck_and_no_network_probe_is_healthy()
    {
        // No healthcheck (health null) and no network probe (null) — running with nothing failing is Healthy.
        HealthEvaluation e = Evaluate("running");

        await Assert.That(e.Health).IsEqualTo(ServerHealth.Healthy);
        await Assert.That(e.Breakdown.Process.Status).IsEqualTo(ProbeStatus.Skipped);
        await Assert.That(e.Breakdown.Network.Status).IsEqualTo(ProbeStatus.Skipped);
    }

    [Test]
    public async Task A_cleanly_exited_container_is_stopped()
    {
        HealthEvaluation e = Evaluate("exited", exitCode: 0);

        await Assert.That(e.Health).IsEqualTo(ServerHealth.Stopped);
        await Assert.That(e.RunState).IsEqualTo(ServerRunState.Stopped);
        await Assert.That(e.Breakdown.Container.Status).IsEqualTo(ProbeStatus.Skipped);
    }

    [Test]
    public async Task A_container_that_exited_nonzero_has_failed()
    {
        HealthEvaluation e = Evaluate("exited", exitCode: 137);

        await Assert.That(e.Health).IsEqualTo(ServerHealth.Failed);
        await Assert.That(e.RunState).IsEqualTo(ServerRunState.Failed);
        await Assert.That(e.Breakdown.Container.Status).IsEqualTo(ProbeStatus.Fail);
        await Assert.That(e.Reason).Contains("137");
    }

    [Test]
    public async Task An_oom_killed_container_has_failed()
    {
        HealthEvaluation e = Evaluate("exited", exitCode: 0, oom: true);

        await Assert.That(e.Health).IsEqualTo(ServerHealth.Failed);
        await Assert.That(e.Reason).Contains("OOM");
    }

    [Test]
    public async Task A_dead_container_has_failed()
    {
        HealthEvaluation e = Evaluate("dead");

        await Assert.That(e.Health).IsEqualTo(ServerHealth.Failed);
        await Assert.That(e.RunState).IsEqualTo(ServerRunState.Failed);
    }

    [Test]
    public async Task A_created_but_never_started_container_is_stopped()
    {
        HealthEvaluation e = Evaluate("created");

        await Assert.That(e.Health).IsEqualTo(ServerHealth.Stopped);
        await Assert.That(e.RunState).IsEqualTo(ServerRunState.Stopped);
    }

    [Test]
    public async Task A_restarting_container_is_starting()
    {
        HealthEvaluation e = Evaluate("restarting");

        await Assert.That(e.Health).IsEqualTo(ServerHealth.Starting);
        await Assert.That(e.RunState).IsEqualTo(ServerRunState.Starting);
    }

    [Test]
    public async Task An_unknown_container_state_maps_to_unknown_runstate_and_a_safe_stopped_health()
    {
        HealthEvaluation e = Evaluate("something-new");

        await Assert.That(e.RunState).IsEqualTo(ServerRunState.Unknown);
        await Assert.That(e.Health).IsEqualTo(ServerHealth.Stopped);
    }

    [Test]
    public async Task Every_evaluation_yields_a_nonempty_reason()
    {
        foreach (string state in new[] { "running", "exited", "created", "restarting", "dead" })
        {
            HealthEvaluation e = Evaluate(state, health: "healthy");
            await Assert.That(e.Reason).IsNotEmpty();
        }
    }
}
