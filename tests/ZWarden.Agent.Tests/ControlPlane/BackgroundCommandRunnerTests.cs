using Microsoft.Extensions.Logging.Abstractions;
using ZWarden.Agent.ControlPlane;

namespace ZWarden.Agent.Tests.ControlPlane;

/// <summary>
/// #248: the SignalR client runs server→Agent handlers one at a time, so a command handler that awaited a whole
/// restart (stop grace, quit, boot) held back every later message — the live-log start/stop, config reads, raw-edit
/// staging, other commands. The runner takes a command off the receive path: <c>Run</c> returns at once, commands
/// run side by side, a fault never escapes, and shutdown drains what is still in flight so its reply can be sent.
/// </summary>
public class BackgroundCommandRunnerTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [Test]
    public async Task Run_returns_before_the_command_finishes()
    {
        BackgroundCommandRunner runner = new(NullLogger.Instance);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        runner.Run(async () =>
        {
            entered.TrySetResult();
            await release.Task;
        });

        await entered.Task.WaitAsync(Wait);
        await Assert.That(runner.InFlight).IsEqualTo(1);
        release.TrySetResult();
        await runner.DrainAsync(CancellationToken.None).WaitAsync(Wait);
        await Assert.That(runner.InFlight).IsEqualTo(0);
    }

    [Test]
    public async Task A_long_command_does_not_hold_back_a_later_one()
    {
        BackgroundCommandRunner runner = new(NullLogger.Instance);
        TaskCompletionSource releaseLong = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource shortRan = new(TaskCreationOptions.RunContinuationsAsynchronously);

        runner.Run(() => releaseLong.Task);           // a restart still running
        runner.Run(() => { shortRan.TrySetResult(); return Task.CompletedTask; });

        await shortRan.Task.WaitAsync(Wait);
        await Assert.That(releaseLong.Task.IsCompleted).IsFalse();
        releaseLong.TrySetResult();
        await runner.DrainAsync(CancellationToken.None).WaitAsync(Wait);
    }

    [Test]
    public async Task A_faulting_command_neither_escapes_nor_stops_later_ones()
    {
        BackgroundCommandRunner runner = new(NullLogger.Instance);
        TaskCompletionSource laterRan = new(TaskCreationOptions.RunContinuationsAsynchronously);

        runner.Run(() => throw new InvalidOperationException("boom"));
        runner.Run(() => { laterRan.TrySetResult(); return Task.CompletedTask; });

        await laterRan.Task.WaitAsync(Wait);
        await runner.DrainAsync(CancellationToken.None).WaitAsync(Wait);
        await Assert.That(runner.InFlight).IsEqualTo(0);
    }

    [Test]
    public async Task Drain_waits_for_in_flight_commands()
    {
        BackgroundCommandRunner runner = new(NullLogger.Instance);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        runner.Run(() => release.Task);

        Task drain = runner.DrainAsync(CancellationToken.None);
        await Task.Delay(100);
        await Assert.That(drain.IsCompleted).IsFalse();

        release.TrySetResult();
        await drain.WaitAsync(Wait);
    }

    [Test]
    public async Task Drain_gives_up_when_shutdown_is_cancelled()
    {
        BackgroundCommandRunner runner = new(NullLogger.Instance);
        TaskCompletionSource never = new(TaskCreationOptions.RunContinuationsAsynchronously);
        runner.Run(() => never.Task);
        using CancellationTokenSource shutdown = new(TimeSpan.FromMilliseconds(100));

        // Bounded by the host's shutdown token — returns rather than hanging the Agent's stop.
        await runner.DrainAsync(shutdown.Token).WaitAsync(Wait);
        await Assert.That(runner.InFlight).IsEqualTo(1);
        never.TrySetResult();
    }
}
