using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ZWarden.Agent.ControlPlane;

/// <summary>
/// Runs dispatched commands off the control-plane connection's receive path (#248). The SignalR client invokes
/// server→Agent handlers one at a time, so a handler that awaited a whole Operation (a restart's stop grace, quit
/// and boot; a SteamCMD update; a backup) held back every later message — the live-log start/stop, config reads,
/// raw-edit staging and further commands — until it finished. <see cref="Run"/> starts the command and returns at
/// once; commands run side by side (Web's per-Server lock already serialises mutating Operations on one Server);
/// a fault is logged, never thrown into the connection; and <see cref="DrainAsync"/> lets shutdown wait for what is
/// still in flight so its reply can still be sent, bounded by the host's shutdown token.
/// </summary>
internal sealed partial class BackgroundCommandRunner
{
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<Task, byte> _inFlight = new();

    // Counted apart from the drain set: raised before a command starts and lowered in its own finally, so it is
    // exact the moment the command's task completes (the set's cleanup continuation may run a beat later).
    private int _running;

    public BackgroundCommandRunner(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>The commands started and not yet finished.</summary>
    public int InFlight => Volatile.Read(ref _running);

    /// <summary>Starts <paramref name="command"/> in the background and returns immediately.</summary>
    public void Run(Func<Task> command)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Built unstarted and tracked before it starts, so it is always counted and drained — never finished on
        // another thread before it was recorded.
        Task<Task> start = new(() => RunGuardedAsync(command));
        Task task = start.Unwrap();
        Interlocked.Increment(ref _running);
        _inFlight.TryAdd(task, 0);
        _ = task.ContinueWith(t => _inFlight.TryRemove(t, out _), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        start.Start(TaskScheduler.Default);
    }

    private async Task RunGuardedAsync(Func<Task> command)
    {
        try
        {
            await command().ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A command fault must never escape into the connection; the lease reaps an unreported operation.
        catch (Exception ex)
        {
            LogCommandFaulted(ex);
        }
#pragma warning restore CA1031
        finally
        {
            Interlocked.Decrement(ref _running);
        }
    }

    /// <summary>Waits for the in-flight commands, giving up when <paramref name="cancellationToken"/> fires.</summary>
    public async Task DrainAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.WhenAll(_inFlight.Keys).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutdown will not wait any longer; an unreported operation is reaped by its lease.
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A dispatched command faulted in the background.")]
    private partial void LogCommandFaulted(Exception exception);
}
