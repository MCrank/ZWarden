using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.Docker;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.LogStreaming;

/// <summary>
/// The default <see cref="IServerLogSubscriptionService"/> (F27): a process-local map of active follows, one per
/// watched Server. Each follow reads the owned container's log stream through <see cref="IContainerRuntime"/>,
/// sanitizes every line (PRD 38), assigns a per-Server monotonic sequence, rate-caps to bound a flooding server,
/// and flushes accumulated lines to ZWarden.Web on an interval as a <see cref="ServerLogBatch"/>. A singleton; the
/// control-plane connection routes <c>Start</c>/<c>Stop</c> into it and supplies the per-connection emitter.
/// </summary>
public sealed partial class ServerLogSubscriptionService : IServerLogSubscriptionService, IAsyncDisposable
{
    private readonly IContainerRuntime _runtime;
    private readonly AgentOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ServerLogSubscriptionService> _logger;
    private readonly ConcurrentDictionary<ServerId, Subscription> _subscriptions = new();

    /// <summary>Creates the service over the container runtime (the follow source), the Agent options (tail, caps,
    /// flush cadence) and a time provider (rate window and retry backoff).</summary>
    public ServerLogSubscriptionService(
        IContainerRuntime runtime,
        IOptions<AgentOptions> options,
        TimeProvider timeProvider,
        ILogger<ServerLogSubscriptionService> logger)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _runtime = runtime;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public void Start(ServerId serverId, IServerLogEmitter emitter)
    {
        ArgumentNullException.ThrowIfNull(emitter);

        Subscription candidate = new(serverId, emitter, _runtime, _options, _timeProvider, _logger);
        if (_subscriptions.TryAdd(serverId, candidate))
        {
            candidate.Begin();
            LogFollowStarted(serverId);
        }

        // Already following (TryAdd lost) — the freshly built candidate never started anything, so drop it.
    }

    /// <inheritdoc />
    public async Task StopAsync(ServerId serverId)
    {
        if (_subscriptions.TryRemove(serverId, out Subscription? subscription))
        {
            await subscription.DisposeAsync().ConfigureAwait(false);
            LogFollowStopped(serverId);
        }
    }

    /// <inheritdoc />
    public async Task StopAllAsync()
    {
        foreach (ServerId serverId in _subscriptions.Keys)
        {
            if (_subscriptions.TryRemove(serverId, out Subscription? subscription))
            {
                await subscription.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await StopAllAsync().ConfigureAwait(false);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Started following logs for server {ServerId}.")]
    private partial void LogFollowStarted(ServerId serverId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Stopped following logs for server {ServerId}.")]
    private partial void LogFollowStopped(ServerId serverId);

    // One Server's live follow: the follow task (with retry), the flush loop, and the sanitize/sequence/rate-cap
    // buffer they share. Self-contained and cancellable — disposing it tears both tasks down cleanly.
    private sealed partial class Subscription : IAsyncDisposable
    {
        private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);

        private readonly ServerId _serverId;
        private readonly IServerLogEmitter _emitter;
        private readonly IContainerRuntime _runtime;
        private readonly AgentOptions _options;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _cts = new();
        private readonly Lock _gate = new();

        private List<ServerLogLine> _pending = [];
        private long _sequence;
        private bool _dropped;
        private DateTimeOffset _windowStart;
        private int _windowCount;
        private Task? _followTask;
        private Task? _flushTask;

        public Subscription(
            ServerId serverId,
            IServerLogEmitter emitter,
            IContainerRuntime runtime,
            AgentOptions options,
            TimeProvider timeProvider,
            ILogger logger)
        {
            _serverId = serverId;
            _emitter = emitter;
            _runtime = runtime;
            _options = options;
            _timeProvider = timeProvider;
            _logger = logger;
            _windowStart = timeProvider.GetUtcNow();
        }

        public void Begin()
        {
            _followTask = Task.Run(() => RunFollowAsync(_cts.Token));
            _flushTask = Task.Run(() => RunFlushAsync(_cts.Token));
        }

        // Follow the owned container's logs, retrying after the stream ends or the container is not (yet) present,
        // so watching a stopped Server picks up when it starts and a restart re-attaches. Cancellation ends it.
        private async Task RunFollowAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await _runtime.FollowServerLogsAsync(_serverId, _options.LogTailLines, OnFrameAsync, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (ContainerNotFoundException)
                {
                    // No owned container for this Server yet (not started, or between restarts) — retry below.
                }
#pragma warning disable CA1031 // A follow failure must not kill the subscription; log it and retry after a backoff.
                catch (Exception ex)
                {
                    LogFollowFailed(_serverId, ex);
                }
#pragma warning restore CA1031

                try
                {
                    await Task.Delay(_options.LogFollowRetryInterval, _timeProvider, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private ValueTask OnFrameAsync(ContainerLogFrame frame, CancellationToken cancellationToken)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            lock (_gate)
            {
                if (now - _windowStart >= OneSecond)
                {
                    _windowStart = now;
                    _windowCount = 0;
                }

                if (_windowCount >= _options.LogMaxLinesPerSecond)
                {
                    // Over the per-second ceiling: coalesce the line away and flag the next batch, bounding the
                    // stream at the source rather than flooding the socket (PRD 38).
                    _dropped = true;
                    return ValueTask.CompletedTask;
                }

                _windowCount++;
                (string text, bool truncated) = LogLineSanitizer.Sanitize(frame.Text, _options.LogLineMaxCharacters);
                _pending.Add(new ServerLogLine(
                    ++_sequence,
                    frame.Timestamp,
                    frame.IsStderr ? LogStreamKind.Stderr : LogStreamKind.Stdout,
                    text,
                    truncated));
            }

            return ValueTask.CompletedTask;
        }

        private async Task RunFlushAsync(CancellationToken cancellationToken)
        {
            using PeriodicTimer timer = new(_options.LogBatchFlushInterval, _timeProvider);
            try
            {
                while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                {
                    await FlushAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // The subscription was torn down — the connection is likely gone, so do not flush on the way out.
            }
        }

        private async Task FlushAsync(CancellationToken cancellationToken)
        {
            List<ServerLogLine> batch;
            bool dropped;
            lock (_gate)
            {
                if (_pending.Count == 0 && !_dropped)
                {
                    return;
                }

                batch = _pending;
                _pending = [];
                dropped = _dropped;
                _dropped = false;
            }

            try
            {
                await _emitter.EmitAsync(_serverId, batch, dropped, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Torn down mid-send; nothing to recover — the stream is transient.
            }
#pragma warning disable CA1031 // A failed send must not kill the flush loop; the next tick tries again.
            catch (Exception ex)
            {
                LogEmitFailed(_serverId, ex);
            }
#pragma warning restore CA1031
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync().ConfigureAwait(false);
            try
            {
                await Task.WhenAll(_followTask ?? Task.CompletedTask, _flushTask ?? Task.CompletedTask).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Teardown is best-effort; a faulted task must not throw out of Dispose.
            catch (Exception ex)
            {
                LogTeardownFault(_serverId, ex);
            }
#pragma warning restore CA1031
            _cts.Dispose();
        }

        [LoggerMessage(Level = LogLevel.Warning, Message = "Following logs for server {ServerId} failed; retrying.")]
        private partial void LogFollowFailed(ServerId serverId, Exception exception);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Emitting a log batch for server {ServerId} failed; will retry on the next flush.")]
        private partial void LogEmitFailed(ServerId serverId, Exception exception);

        [LoggerMessage(Level = LogLevel.Debug, Message = "A log follow task for server {ServerId} faulted during teardown.")]
        private partial void LogTeardownFault(ServerId serverId, Exception exception);
    }
}
