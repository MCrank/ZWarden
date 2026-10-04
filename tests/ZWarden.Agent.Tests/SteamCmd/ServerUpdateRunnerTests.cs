using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.ControlPlane;
using ZWarden.Agent.Docker;
using ZWarden.Agent.SteamCmd;
using ZWarden.Agent.Tests.ControlPlane;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests.SteamCmd;

/// <summary>
/// F17: the SteamCMD update runner. It writes the control-file, restarts the container, then polls the log and
/// turns SteamCMD's output into progress + a terminal outcome (parsed, never an exit code — ADR 0009). Driven
/// with scripted log snapshots, a zero poll interval and a real clock, so it is deterministic and fast.
/// </summary>
public class ServerUpdateRunnerTests
{
    private static ServerUpdateRunner Runner(
        ScriptedRuntime runtime, RecordingPaths paths, TimeSpan? timeout = null,
        FakeServerRestartCoordinator? coordinator = null, TimeProvider? clock = null) =>
        new(
            runtime,
            paths,
            coordinator ?? new FakeServerRestartCoordinator(),
            Options.Create(new AgentOptions
            {
                UpdatePollInterval = TimeSpan.Zero,
                UpdateTimeout = timeout ?? TimeSpan.FromMinutes(5),
            }),
            clock ?? TimeProvider.System,
            NullLogger<ServerUpdateRunner>.Instance);

    private static string Begin(OperationId op) => $"[zwarden] steamcmd update session {op} begin";
    private static string EndOk(OperationId op) => $"[zwarden] steamcmd update session {op} end (success)";
    private static string EndFail(OperationId op) => $"[zwarden] steamcmd update session {op} end (failure)";
    private static string Progress(int pct) => $" Update state (0x61) downloading, progress: {pct}.00 (x / y)";

    [Test]
    public async Task It_writes_the_request_restarts_reports_progress_and_reads_the_build_id_on_success()
    {
        ServerId serverId = ServerId.New();
        OperationId op = OperationId.New();
        var runtime = new ScriptedRuntime();
        runtime.Logs.Enqueue(string.Join('\n', Begin(op), Progress(6)));
        runtime.Logs.Enqueue(string.Join('\n', Begin(op), Progress(43), "Success! App '380870' fully installed", EndOk(op)));
        var paths = new RecordingPaths { BuildId = "24909836" };
        var reporter = new RecordingReporter();

        ServerUpdateOutcome outcome = await Runner(runtime, paths).RunAsync(serverId, op, reporter, CancellationToken.None);

        await Assert.That(outcome.Succeeded).IsTrue();
        await Assert.That(outcome.InstalledBuildId).IsEqualTo("24909836");
        await Assert.That(paths.WroteRequestFor).IsEqualTo(serverId);
        await Assert.That(runtime.RestartedServerId).IsEqualTo(serverId);
        await Assert.That(reporter.Percents).Contains(6);
        await Assert.That(reporter.Percents).Contains(43);
    }

    [Test]
    public async Task It_reads_the_build_it_is_replacing_before_the_update_runs()
    {
        // #273: "build A → B" on the page. Read here, before SteamCMD runs, so it is observed — the control plane's
        // stored build may already be the new one (a metrics report can land before the completion).
        OperationId op = OperationId.New();
        var runtime = new ScriptedRuntime();
        runtime.Logs.Enqueue(string.Join('\n', Begin(op), "Success! App '380870' fully installed", EndOk(op)));
        var paths = new RecordingPaths { BuildIdBefore = "24909836", BuildId = "25485538" };

        ServerUpdateOutcome outcome = await Runner(runtime, paths).RunAsync(ServerId.New(), op, new RecordingReporter(), CancellationToken.None);

        await Assert.That(outcome.PreviousBuildId).IsEqualTo("24909836");
        await Assert.That(outcome.InstalledBuildId).IsEqualTo("25485538");
    }

    [Test]
    public async Task An_update_restarts_the_existing_container_so_a_pinned_branch_is_kept()
    {
        // #258/#273: the branch lives in the container's ZW_PZ_BETA, set at create. An update only restarts that same
        // container into SteamCMD (never recreates it — every other runtime verb here throws), so a pinned 42.19 server
        // updates within 42.19 and a public one follows public.
        ServerId serverId = ServerId.New();
        OperationId op = OperationId.New();
        var runtime = new ScriptedRuntime();
        runtime.Logs.Enqueue(string.Join('\n', Begin(op), "Success! App '380870' fully installed", EndOk(op)));

        ServerUpdateOutcome outcome = await Runner(runtime, new RecordingPaths { BuildId = "24929695" })
            .RunAsync(serverId, op, new RecordingReporter(), CancellationToken.None);

        await Assert.That(outcome.Succeeded).IsTrue();
        await Assert.That(runtime.RestartedServerId).IsEqualTo(serverId);
    }

    [Test]
    public async Task A_long_stretch_without_steamcmd_progress_still_reports_so_the_lease_holds()
    {
        // #280: the control plane renews the Operation's lease only on a progress report. SteamCMD can go minutes
        // without a progress line (a 2-minute "timed out waiting for update to start", a slow bootstrap), so the
        // runner re-reports at least every minute while it waits.
        OperationId op = OperationId.New();
        var runtime = new ScriptedRuntime();
        for (int i = 0; i < 4; i++)
        {
            runtime.Logs.Enqueue(Begin(op)); // no progress lines at all
        }

        runtime.Logs.Enqueue(string.Join('\n', Begin(op), "Success! App '380870' fully installed", EndOk(op)));
        var reporter = new RecordingReporter();
        var clock = new SteppingClock(DateTimeOffset.UnixEpoch, TimeSpan.FromSeconds(61));

        ServerUpdateOutcome outcome = await Runner(runtime, new RecordingPaths { BuildId = "1" }, TimeSpan.FromHours(1), clock: clock)
            .RunAsync(ServerId.New(), op, reporter, CancellationToken.None);

        await Assert.That(outcome.Succeeded).IsTrue();
        await Assert.That(reporter.Percents.Count).IsGreaterThanOrEqualTo(3);
        await Assert.That(reporter.Statuses).Contains("Waiting for SteamCMD…");
    }

    [Test]
    public async Task It_warns_players_before_taking_the_server_down_for_the_update()
    {
        // #114: an update restart inherits the graceful player broadcast.
        ServerId serverId = ServerId.New();
        OperationId op = OperationId.New();
        var runtime = new ScriptedRuntime();
        runtime.Logs.Enqueue(string.Join('\n', Begin(op), "Success! App '380870' fully installed", EndOk(op)));
        var coordinator = new FakeServerRestartCoordinator();

        await Runner(runtime, new RecordingPaths { BuildId = "1" }, coordinator: coordinator)
            .RunAsync(serverId, op, new RecordingReporter(), CancellationToken.None);

        await Assert.That(coordinator.WarnCount).IsEqualTo(1);
        await Assert.That(coordinator.LastWarnedServerId).IsEqualTo(serverId);
    }

    [Test]
    public async Task A_failed_update_returns_the_error_line_as_the_reason()
    {
        OperationId op = OperationId.New();
        var runtime = new ScriptedRuntime();
        runtime.Logs.Enqueue(string.Join('\n', Begin(op), "Error! App '380870' state is 0x202 after update job.", EndFail(op)));

        ServerUpdateOutcome outcome = await Runner(runtime, new RecordingPaths())
            .RunAsync(ServerId.New(), op, new RecordingReporter(), CancellationToken.None);

        await Assert.That(outcome.Succeeded).IsFalse();
        await Assert.That(outcome.FailureReason).Contains("0x202");
        await Assert.That(outcome.InstalledBuildId).IsNull();
    }

    [Test]
    public async Task No_container_to_restart_fails_with_an_actionable_reason_and_never_polls()
    {
        var runtime = new ScriptedRuntime { RestartThrowsNotFound = true };

        ServerUpdateOutcome outcome = await Runner(runtime, new RecordingPaths())
            .RunAsync(ServerId.New(), OperationId.New(), new RecordingReporter(), CancellationToken.None);

        await Assert.That(outcome.Succeeded).IsFalse();
        await Assert.That(outcome.FailureReason).Contains("Provision");
        await Assert.That(runtime.LogReadCount).IsEqualTo(0);
    }

    [Test]
    public async Task It_times_out_when_the_update_never_completes()
    {
        OperationId op = OperationId.New();
        var runtime = new ScriptedRuntime();
        runtime.Logs.Enqueue(string.Join('\n', Begin(op), Progress(10))); // perpetual pending (last snapshot repeats)
        var reporter = new RecordingReporter();

        ServerUpdateOutcome outcome = await Runner(runtime, new RecordingPaths(), timeout: TimeSpan.Zero)
            .RunAsync(ServerId.New(), op, reporter, CancellationToken.None);

        await Assert.That(outcome.Succeeded).IsFalse();
        await Assert.That(outcome.FailureReason).Contains("allotted time");
        await Assert.That(reporter.Percents).Contains(10); // progress reported before the deadline fired
    }

    // --- fakes -------------------------------------------------------------------

    private sealed class RecordingReporter : IOperationProgressReporter
    {
        public List<int> Percents { get; } = [];

        public List<string?> Statuses { get; } = [];

        public Task ReportAsync(OperationId operationId, int percentComplete, string? statusLine, CancellationToken cancellationToken)
        {
            Percents.Add(percentComplete);
            Statuses.Add(statusLine);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPaths : IServerInstallPaths
    {
        public string? BuildId { get; set; }

        // The manifest's build before the update ran (#273); the first read returns it when set.
        public string? BuildIdBefore { get; set; }

        private bool _readBefore;

        public ServerId? WroteRequestFor { get; private set; }

        public void WriteUpdateRequest(ServerId serverId, OperationId operationId) => WroteRequestFor = serverId;

        public string? ReadInstalledBuildId(ServerId serverId)
        {
            if (!_readBefore && BuildIdBefore is not null)
            {
                _readBefore = true;
                return BuildIdBefore;
            }

            return BuildId;
        }

        public string GetWorkshopContentRoot(ServerId serverId) => throw new NotSupportedException();

        public IReadOnlyDictionary<string, DateTimeOffset> ReadWorkshopInstalledTimes(ServerId serverId) => throw new NotSupportedException();
    }

    // A minimal IContainerRuntime: only restart + read-logs are exercised; the rest is loud if the runner drifts.
    private sealed class ScriptedRuntime : IContainerRuntime
    {
        public Queue<string> Logs { get; } = new();

        public bool RestartThrowsNotFound { get; set; }

        public ServerId? RestartedServerId { get; private set; }

        public int LogReadCount { get; private set; }

        public Task RestartAsync(ServerId serverId, CancellationToken cancellationToken)
        {
            if (RestartThrowsNotFound)
            {
                throw new ContainerNotFoundException(serverId);
            }

            RestartedServerId = serverId;
            return Task.CompletedTask;
        }

        public Task<string> ReadServerLogsAsync(ServerId serverId, DateTimeOffset? since, CancellationToken cancellationToken)
        {
            LogReadCount++;
            // Dequeue the next snapshot, or repeat the last one so a "pending forever" script keeps polling.
            string snapshot = Logs.Count > 1 ? Logs.Dequeue() : Logs.Peek();
            return Task.FromResult(snapshot);
        }

        public Task<string?> ResolveNetworkAddressAsync(ServerId serverId, string networkName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task FollowServerLogsAsync(ServerId serverId, int tailLines, Func<ContainerLogFrame, CancellationToken, ValueTask> onFrame, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DockerHealth> ProbeHealthAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ManagedContainer>> ListManagedAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<HostMemory> ReadHostMemoryAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ObservedContainer>> InspectManagedAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PortAllocation> ClaimRequestedPortsAsync(int gamePort, ServerId forServer, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ServerContainer?> InspectServerAsync(ServerId serverId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RemoveAsync(ServerId serverId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PortAllocation> AllocateNextPortsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> CreateAsync(PzContainerSpec spec, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task StartAsync(string containerId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task StopAsync(string containerId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RestartAsync(string containerId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task StartAsync(ServerId serverId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task StopAsync(ServerId serverId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    // A clock that moves forward by a fixed step on every read, so a zero-interval poll loop sees minutes pass.
    private sealed class SteppingClock(DateTimeOffset start, TimeSpan step) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow()
        {
            DateTimeOffset now = _now;
            _now += step;
            return now;
        }
    }
}
