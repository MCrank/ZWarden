using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;

namespace ZWarden.Agent.Health;

/// <summary>
/// The observed facts a single Server's four probes gathered (F16), the pure input to
/// <see cref="ServerHealthEvaluator"/>. <see cref="ContainerState"/> and <see cref="ContainerHealth"/> come from
/// Docker <c>inspect</c> (<c>State.Status</c> and <c>State.Health.Status</c>); <see cref="PortsReachable"/> is
/// the host-side network probe (<c>null</c> when it was not run — e.g. the container is not running).
/// </summary>
/// <param name="ContainerState">Docker's container state, e.g. <c>running</c>, <c>exited</c>, <c>created</c>,
/// <c>restarting</c>, <c>dead</c>.</param>
/// <param name="ContainerHealth">The image HEALTHCHECK verdict: <c>healthy</c>/<c>unhealthy</c>/<c>starting</c>,
/// or <c>null</c> when the image declares none.</param>
/// <param name="ExitCode">The last exit code (0 unless the container has exited abnormally).</param>
/// <param name="OomKilled">Whether the container was OOM-killed.</param>
/// <param name="PortsReachable">The network probe result, or <c>null</c> when it was not run.</param>
public sealed record HealthProbeFacts(
    string ContainerState,
    string? ContainerHealth,
    long ExitCode,
    bool OomKilled,
    bool? PortsReachable);

/// <summary>The rolled-up verdict for one Server (F16): its <see cref="ServerHealth"/>, its coarse
/// <see cref="ServerRunState"/>, the four-probe <see cref="HealthBreakdown"/>, and a short human reason.</summary>
/// <param name="Health">The hierarchical health rollup.</param>
/// <param name="RunState">The coarse run-state derived from the same facts.</param>
/// <param name="Breakdown">The four probe verdicts behind the rollup.</param>
/// <param name="Reason">A short, non-secret summary of the rollup.</param>
public sealed record HealthEvaluation(
    ServerHealth Health,
    ServerRunState RunState,
    HealthBreakdown Breakdown,
    string Reason);

/// <summary>
/// Rolls the four probe facts up into a Server's hierarchical <see cref="ServerHealth"/> and coarse
/// <see cref="ServerRunState"/> (F16). It is a <b>pure</b> function of <see cref="HealthProbeFacts"/> — no Docker,
/// no clock, no I/O — so the whole truth table is unit-tested. The hierarchy is container → process → startup →
/// network: a container that is not running is <see cref="ServerHealth.Stopped"/> (clean) or
/// <see cref="ServerHealth.Failed"/> (crash/OOM/dead); a running one is <see cref="ServerHealth.Starting"/> while
/// inside its startup window, <see cref="ServerHealth.Failed"/> if its process healthcheck is unhealthy,
/// <see cref="ServerHealth.Degraded"/> if it is up but a port is unreachable, and <see cref="ServerHealth.Healthy"/>
/// when every probe passes.
/// </summary>
public static class ServerHealthEvaluator
{
    // 128 + SIGTERM(15): the exit code the PZ entrypoint's graceful-stop path yields when `docker stop` delivers
    // SIGTERM and the JVM saves then quits cleanly (#200). It is a normal stop, not a crash — unlike 137
    // (128 + SIGKILL), which means the stop timeout was exceeded and the save was truncated, and stays "bad".
    private const long GracefulSigtermExitCode = 143;

    // A container that exited on its own terms: a clean 0, or the graceful-stop SIGTERM code. OOM and a "dead"
    // state are handled separately and always override this — an OOM-killed container can still report 143.
    private static bool IsCleanExit(long exitCode) => exitCode is 0 or GracefulSigtermExitCode;

    /// <summary>Evaluates one Server's probe facts into its health rollup, run-state, breakdown and reason.</summary>
    public static HealthEvaluation Evaluate(HealthProbeFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        string state = facts.ContainerState?.Trim().ToLowerInvariant() ?? string.Empty;
        string? health = facts.ContainerHealth?.Trim().ToLowerInvariant();
        bool running = state == "running";
        bool exitedBadly = facts.OomKilled || state == "dead" || !IsCleanExit(facts.ExitCode);

        HealthBreakdown breakdown = new(
            Container: ContainerCheck(state, facts, exitedBadly),
            Process: ProcessCheck(running, health),
            Startup: StartupCheck(running, health),
            Network: NetworkCheck(facts.PortsReachable));

        (ServerHealth serverHealth, ServerRunState runState) = Roll(state, health, running, exitedBadly, facts.PortsReachable);
        return new HealthEvaluation(serverHealth, runState, breakdown, Reason(serverHealth, breakdown));
    }

    private static (ServerHealth Health, ServerRunState RunState) Roll(
        string state, string? health, bool running, bool exitedBadly, bool? portsReachable)
    {
        if (running)
        {
            return health switch
            {
                "unhealthy" => (ServerHealth.Failed, ServerRunState.Running),
                "starting" => (ServerHealth.Starting, ServerRunState.Starting),
                _ => portsReachable == false
                    ? (ServerHealth.Degraded, ServerRunState.Running)
                    : (ServerHealth.Healthy, ServerRunState.Running),
            };
        }

        return state switch
        {
            "restarting" => (ServerHealth.Starting, ServerRunState.Starting),
            "created" => (ServerHealth.Stopped, ServerRunState.Stopped),
            "exited" or "dead" => exitedBadly
                ? (ServerHealth.Failed, ServerRunState.Failed)
                : (ServerHealth.Stopped, ServerRunState.Stopped),
            "paused" => (ServerHealth.Degraded, ServerRunState.Stopping),
            "removing" => (ServerHealth.Stopped, ServerRunState.Stopping),
            _ => (ServerHealth.Stopped, ServerRunState.Unknown),
        };
    }

    private static ProbeCheck ContainerCheck(string state, HealthProbeFacts facts, bool exitedBadly) => state switch
    {
        "running" => new ProbeCheck(ProbeStatus.Pass),
        "restarting" => new ProbeCheck(ProbeStatus.Warn, "the container is restarting"),
        "created" => new ProbeCheck(ProbeStatus.Skipped, "the container has not been started"),
        "paused" => new ProbeCheck(ProbeStatus.Warn, "the container is paused"),
        "removing" => new ProbeCheck(ProbeStatus.Warn, "the container is being removed"),
        "exited" or "dead" => exitedBadly
            ? new ProbeCheck(ProbeStatus.Fail, DescribeExit(facts))
            : new ProbeCheck(ProbeStatus.Skipped, "the container is stopped"),
        _ => new ProbeCheck(ProbeStatus.Skipped, $"container state '{state}'"),
    };

    private static ProbeCheck ProcessCheck(bool running, string? health)
    {
        if (!running)
        {
            return new ProbeCheck(ProbeStatus.Skipped, "the container is not running");
        }

        return health switch
        {
            "healthy" => new ProbeCheck(ProbeStatus.Pass),
            "unhealthy" => new ProbeCheck(ProbeStatus.Fail, "the process healthcheck reports unhealthy"),
            "starting" => new ProbeCheck(ProbeStatus.Warn, "the process is starting"),
            _ => new ProbeCheck(ProbeStatus.Skipped, "the image declares no process healthcheck"),
        };
    }

    private static ProbeCheck StartupCheck(bool running, string? health)
    {
        if (!running)
        {
            return new ProbeCheck(ProbeStatus.Skipped, "the container is not running");
        }

        return health == "starting"
            ? new ProbeCheck(ProbeStatus.Warn, "inside the startup window")
            : new ProbeCheck(ProbeStatus.Pass);
    }

    private static ProbeCheck NetworkCheck(bool? portsReachable) => portsReachable switch
    {
        true => new ProbeCheck(ProbeStatus.Pass),
        false => new ProbeCheck(ProbeStatus.Fail, "a published game/query port is unreachable"),
        null => new ProbeCheck(ProbeStatus.Skipped, "the network probe was not run"),
    };

    private static string DescribeExit(HealthProbeFacts facts)
    {
        if (facts.OomKilled)
        {
            return "the container was OOM-killed";
        }

        return facts.ExitCode != 0
            ? $"the container exited with code {facts.ExitCode}"
            : "the container is dead";
    }

    private static string Reason(ServerHealth health, HealthBreakdown breakdown) => health switch
    {
        ServerHealth.Healthy => "All probes pass.",
        ServerHealth.Starting => "The server is starting.",
        ServerHealth.Stopped => "The server is stopped.",
        ServerHealth.Failed => FirstDetail(breakdown, ProbeStatus.Fail) ?? "The server has failed.",
        ServerHealth.Degraded => FirstDetail(breakdown, ProbeStatus.Fail)
            ?? FirstDetail(breakdown, ProbeStatus.Warn)
            ?? "The server is degraded.",
        _ => "The server state is unknown.",
    };

    private static string? FirstDetail(HealthBreakdown breakdown, ProbeStatus status)
    {
        foreach (ProbeCheck check in new[] { breakdown.Container, breakdown.Process, breakdown.Startup, breakdown.Network })
        {
            if (check.Status == status && check.Detail is { Length: > 0 } detail)
            {
                return Capitalize(detail);
            }
        }

        return null;
    }

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..] + ".";
}
