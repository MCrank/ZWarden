using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Servers;
using ZWarden.Web.Components.Servers;

namespace ZWarden.Web.Tests.Servers;

/// <summary>
/// #249: the live header's status. The Agent observes a safe stop as Running for its whole grace window (the
/// container is still up), so a transitional label comes from the Server's in-flight lifecycle Operation; while any
/// mutating Operation holds the per-server lock every lifecycle button is off (the service would refuse it as busy),
/// otherwise the buttons follow the observed state exactly as the static header always did.
/// </summary>
public class ServerLiveStatusTests
{
    [Test]
    public async Task With_nothing_in_flight_it_shows_the_observed_state_and_its_buttons()
    {
        ServerStatusView running = ServerLiveStatus.Resolve(ServerRunState.Running, activeOperation: null);

        await Assert.That(running.Label).IsEqualTo("RUNNING");
        await Assert.That(running.Tone).IsEqualTo(ServerRunState.Running);
        await Assert.That(running.Busy).IsFalse();
        await Assert.That(running.CanStart).IsFalse();
        await Assert.That(running.CanStop).IsTrue();
        await Assert.That(running.CanRestart).IsTrue();

        ServerStatusView stopped = ServerLiveStatus.Resolve(ServerRunState.Stopped, activeOperation: null);
        await Assert.That(stopped.CanStart).IsTrue();
        await Assert.That(stopped.CanStop).IsFalse();
        await Assert.That(stopped.CanRestart).IsFalse();
    }

    [Test]
    [Arguments(OperationKind.StopServer, "STOPPING")]
    [Arguments(OperationKind.RestartServer, "RESTARTING")]
    [Arguments(OperationKind.StartServer, "STARTING")]
    [Arguments(OperationKind.UpdateServer, "UPDATING")]
    [Arguments(OperationKind.Restore, "RESTORING")]
    public async Task A_lifecycle_operation_in_flight_shows_its_transition_as_busy(OperationKind kind, string label)
    {
        // Observed Running throughout a safe stop's grace window — the label still says what is happening.
        ServerStatusView view = ServerLiveStatus.Resolve(ServerRunState.Running, kind);

        await Assert.That(view.Label).IsEqualTo(label);
        await Assert.That(view.Busy).IsTrue();
        await Assert.That(view.ToneKey).IsEqualTo("busy");
    }

    [Test]
    public async Task The_in_flight_operations_status_line_rides_along_as_detail()
    {
        // #254: a header Restart's countdown otherwise looks stuck on RESTARTING for five minutes.
        ServerStatusView view = ServerLiveStatus.Resolve(
            ServerRunState.Running, OperationKind.RestartServer, "Restarting in 240 seconds.");

        await Assert.That(view.Detail).IsEqualTo("Restarting in 240 seconds.");
    }

    [Test]
    public async Task There_is_no_detail_without_an_operation_in_flight()
    {
        await Assert.That(ServerLiveStatus.Resolve(ServerRunState.Running, null, "stale line").Detail).IsNull();
        await Assert.That(ServerLiveStatus.Resolve(ServerRunState.Running, OperationKind.RestartServer, "  ").Detail).IsNull();
    }

    [Test]
    public async Task Any_operation_holding_the_lock_disables_every_lifecycle_button()
    {
        ServerStatusView view = ServerLiveStatus.Resolve(ServerRunState.Running, OperationKind.ConfigApply);

        // A config apply isn't a run-state transition, so the badge keeps the observed state...
        await Assert.That(view.Label).IsEqualTo("RUNNING");
        await Assert.That(view.ToneKey).IsEqualTo("running");
        // ...but the lock is held, so no lifecycle action can be taken until it finishes.
        await Assert.That(view.Busy).IsTrue();
        await Assert.That(view.CanStart).IsFalse();
        await Assert.That(view.CanStop).IsFalse();
        await Assert.That(view.CanRestart).IsFalse();
    }

    [Test]
    [Arguments(ServerRunState.Running, "running")]
    [Arguments(ServerRunState.Stopped, "stopped")]
    [Arguments(ServerRunState.Starting, "busy")]
    [Arguments(ServerRunState.Stopping, "busy")]
    [Arguments(ServerRunState.Failed, "unhealthy")]
    [Arguments(ServerRunState.Unknown, "unknown")]
    public async Task The_tone_key_matches_the_status_ramp(ServerRunState state, string tone)
    {
        await Assert.That(ServerLiveStatus.Resolve(state, activeOperation: null).ToneKey).IsEqualTo(tone);
    }

    [Test]
    public async Task A_fleet_row_resolves_against_its_own_servers_in_flight_operation_only()
    {
        // #253: the fleet reads every in-flight Operation once and each row picks out its own Server's.
        ServerId restarting = ServerId.New();
        ServerId idle = ServerId.New();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        IReadOnlyDictionary<ServerId, Operation> active = ServerLiveStatus.ActiveByServer(
        [
            Operation.Enqueue(AgentId.New(), OperationKind.RestartServer, isMutating: true, "restart", now, restarting),
            Operation.Enqueue(AgentId.New(), OperationKind.DiagnosticsPing, isMutating: false, "unscoped", now),
        ]);

        await Assert.That(ServerLiveStatus.Resolve(ServerRunState.Running, restarting, active).Label).IsEqualTo("RESTARTING");
        await Assert.That(ServerLiveStatus.Resolve(ServerRunState.Running, idle, active).Label).IsEqualTo("RUNNING");
        await Assert.That(ServerLiveStatus.Resolve(ServerRunState.Running, idle, active).CanRestart).IsTrue();
    }
}
