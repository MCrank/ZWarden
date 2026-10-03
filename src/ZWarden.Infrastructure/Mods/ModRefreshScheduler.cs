using System.Threading.Channels;
using ZWarden.Application.Mods;

namespace ZWarden.Infrastructure.Mods;

/// <summary>
/// The in-process <see cref="IModRefreshScheduler"/> (#290 D1): a bounded channel the <see cref="ModRefreshWorker"/>
/// drains. A request equal to one still waiting is dropped, which coalesces a burst of triggers for one Server. When
/// the channel is full the oldest request is dropped. Delayed requests wait on the <see cref="TimeProvider"/> and are
/// abandoned on shutdown. Nothing survives a restart, by design: the next boot or Agent reconnect re-requests it.
/// </summary>
public sealed class ModRefreshScheduler : IModRefreshScheduler, IDisposable
{
    private const int DefaultCapacity = 1024;

    private readonly Channel<ModRefreshRequest> _channel;
    private readonly HashSet<ModRefreshRequest> _waiting = [];
    private readonly Lock _gate = new();
    private readonly TimeProvider _clock;
    private readonly CancellationTokenSource _shutdown = new();

    /// <summary>A scheduler with the default capacity.</summary>
    public ModRefreshScheduler(TimeProvider clock)
        : this(clock, DefaultCapacity)
    {
    }

    /// <summary>A scheduler holding at most <paramref name="capacity"/> waiting requests.</summary>
    public ModRefreshScheduler(TimeProvider clock, int capacity)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
        _channel = Channel.CreateBounded<ModRefreshRequest>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
            },
            Forget);
    }

    /// <summary>How many requests are waiting.</summary>
    public int Count => _channel.Reader.Count;

    /// <inheritdoc />
    public void Enqueue(ModRefreshRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            if (!_waiting.Add(request))
            {
                return;
            }

            if (!_channel.Writer.TryWrite(request))
            {
                _waiting.Remove(request);
            }
        }
    }

    /// <inheritdoc />
    public void EnqueueAfter(ModRefreshRequest request, TimeSpan delay)
    {
        ArgumentNullException.ThrowIfNull(request);
        _ = EnqueueLaterAsync(request, delay);
    }

    /// <summary>Waits for the next request (the worker's read).</summary>
    public async ValueTask<ModRefreshRequest> ReadAsync(CancellationToken cancellationToken)
    {
        ModRefreshRequest request = await _channel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        Forget(request);
        return request;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _shutdown.Cancel();
        _shutdown.Dispose();
        _channel.Writer.TryComplete();
    }

    private async Task EnqueueLaterAsync(ModRefreshRequest request, TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay, _clock, _shutdown.Token).ConfigureAwait(false);
            Enqueue(request);
        }
        catch (OperationCanceledException)
        {
            // Shutting down: the follow-up is abandoned.
        }
        catch (ObjectDisposedException)
        {
            // Disposed while waiting.
        }
    }

    private void Forget(ModRefreshRequest request)
    {
        lock (_gate)
        {
            _waiting.Remove(request);
        }
    }
}
