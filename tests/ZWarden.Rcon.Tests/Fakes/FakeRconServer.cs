using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ZWarden.Rcon.Tests.Fakes;

/// <summary>
/// A loopback TCP server that speaks Source RCON the way PZ (Build 42.20.2) does, faithfully to
/// research/project-zomboid-runtime.md §7 - it is the executable spec of the four traps F18 must
/// survive. It uses its own raw frame codec (not <c>ZWarden.Rcon</c>'s internal one) so it stays an
/// independent check on the client. Configure it, <c>await Start()</c>, point an
/// <see cref="RconEndpoint"/> at <see cref="Host"/>/<see cref="Port"/>, and dispose it to stop.
/// </summary>
internal sealed class FakeRconServer : IAsyncDisposable
{
    // PZ's real response-chunk cap. A body at or above this streams as multiple chunks sharing one
    // id/type=0; the client detects the end only by a chunk shorter than this (there is no sentinel).
    private const int ChunkSize = 4086;

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<int, byte> _active = new();
    private Task? _acceptLoop;
    private int _nextConnectionKey;
    private int _acceptedCount;
    private int _maxConcurrent;

    /// <summary>The password an AUTH packet must carry to succeed. Empty means every auth fails,
    /// modelling PZ's "empty RCONPassword disables RCON".</summary>
    public string Password { get; init; } = "s3cret";

    /// <summary>Maps a command to its response. Return <c>null</c> or an empty string to model PZ
    /// sending <b>no packet at all</b> for an empty result (quirk 3). Default: echo nothing but the
    /// command name back, so a caller gets a non-empty reply unless it asks for the empty case.</summary>
    public Func<string, string?> Responder { get; init; } = cmd => $"ok: {cmd}";

    /// <summary>Milliseconds to wait before writing a command's response, modelling tick-serialized
    /// execution (quirk 8). Keep below the client's idle window or the client reads it as empty.</summary>
    public int TickDelayMs { get; init; }

    /// <summary>PZ's hard cap: the 6th simultaneous connection is accepted then immediately closed
    /// (quirk 7). Overridable so a test can force the cap at a smaller number.</summary>
    public int MaxConnections { get; init; } = 5;

    /// <summary>When positive, the server closes a connection after serving this many commands,
    /// letting a test exercise the client's reconnect path. Zero keeps the socket alive
    /// indefinitely, as PZ does (quirk 5).</summary>
    public int CloseAfterCommands { get; init; }

    public FakeRconServer() => _listener = new TcpListener(IPAddress.Loopback, 0);

    public string Host => ((IPEndPoint)_listener.LocalEndpoint).Address.ToString();

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Total sockets accepted, including ones instantly closed for exceeding the cap.</summary>
    public int AcceptedCount => Volatile.Read(ref _acceptedCount);

    /// <summary>The high-water mark of simultaneously-served connections.</summary>
    public int MaxConcurrentConnections => Volatile.Read(ref _maxConcurrent);

    public FakeRconServer Start()
    {
        _listener.Start();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
        return this;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient socket;
            try
            {
                socket = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (SocketException)
            {
                return;
            }

            Interlocked.Increment(ref _acceptedCount);

            if (_active.Count >= MaxConnections)
            {
                // Accept-then-close: the client sees a connect that instantly EOFs (quirk 7).
                socket.Dispose();
                continue;
            }

            int key = Interlocked.Increment(ref _nextConnectionKey);
            _active[key] = 0;
            UpdateHighWater();
            _ = Task.Run(() => ServeAsync(socket, key, ct), ct);
        }
    }

    private async Task ServeAsync(TcpClient socket, int key, CancellationToken ct)
    {
        try
        {
            socket.NoDelay = true;
            NetworkStream stream = socket.GetStream();
            int commandsServed = 0;
            while (!ct.IsCancellationRequested)
            {
                Frame? request = await ReadFrameAsync(stream, ct).ConfigureAwait(false);
                if (request is null)
                {
                    return; // client closed
                }

                Frame frame = request.Value;
                if (frame.Type == 3) // SERVERDATA_AUTH
                {
                    await HandleAuthAsync(stream, frame, ct).ConfigureAwait(false);
                    if (frame.Body != Password || Password.Length == 0)
                    {
                        return; // PZ closes the socket after a failed auth
                    }
                }
                else if (frame.Type == 2) // SERVERDATA_EXECCOMMAND
                {
                    await HandleExecAsync(stream, frame, ct).ConfigureAwait(false);
                    if (CloseAfterCommands > 0 && ++commandsServed >= CloseAfterCommands)
                    {
                        return; // drop the socket to exercise the client's reconnect path
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException)
        {
            // Client vanished or we are shutting down.
        }
        finally
        {
            _active.TryRemove(key, out _);
            socket.Dispose();
        }
    }

    private async Task HandleAuthAsync(NetworkStream stream, Frame request, CancellationToken ct)
    {
        // The (ignored) leading empty RESPONSE_VALUE, then the AUTH_RESPONSE: request id on success,
        // -1 on failure - exactly PZ's sequence.
        await WriteFrameAsync(stream, new Frame(request.Id, 0, string.Empty), ct).ConfigureAwait(false);
        bool ok = Password.Length != 0 && request.Body == Password;
        await WriteFrameAsync(stream, new Frame(ok ? request.Id : -1, 2, string.Empty), ct).ConfigureAwait(false);
    }

    private async Task HandleExecAsync(NetworkStream stream, Frame request, CancellationToken ct)
    {
        if (TickDelayMs > 0)
        {
            await Task.Delay(TickDelayMs, ct).ConfigureAwait(false);
        }

        string? response = Responder(request.Body);
        if (string.IsNullOrEmpty(response))
        {
            return; // quirk 3: an empty result sends no packet at all
        }

        byte[] bytes = Encoding.UTF8.GetBytes(response);
        int offset = 0;
        do
        {
            int take = Math.Min(ChunkSize, bytes.Length - offset);
            string chunk = Encoding.UTF8.GetString(bytes, offset, take);
            await WriteFrameAsync(stream, new Frame(request.Id, 0, chunk), ct).ConfigureAwait(false);
            offset += take;
        }
        while (offset < bytes.Length);
    }

    private void UpdateHighWater()
    {
        int now = _active.Count;
        int seen;
        do
        {
            seen = Volatile.Read(ref _maxConcurrent);
            if (now <= seen)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref _maxConcurrent, now, seen) != seen);
    }

    // --- raw frame codec (independent of the code under test) ------------------------------------

    private readonly record struct Frame(int Id, int Type, string Body);

    private static async Task<Frame?> ReadFrameAsync(NetworkStream stream, CancellationToken ct)
    {
        byte[] lengthPrefix = new byte[4];
        if (!await ReadExactAsync(stream, lengthPrefix, ct).ConfigureAwait(false))
        {
            return null;
        }

        int size = BinaryPrimitives.ReadInt32LittleEndian(lengthPrefix);
        byte[] payload = new byte[size];
        if (!await ReadExactAsync(stream, payload, ct).ConfigureAwait(false))
        {
            return null;
        }

        int id = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(0, 4));
        int type = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(4, 4));
        int bodyLength = size - 10; // minus id(4) + type(4) + two NULs(2)
        string body = bodyLength <= 0 ? string.Empty : Encoding.UTF8.GetString(payload, 8, bodyLength);
        return new Frame(id, type, body);
    }

    private static async Task WriteFrameAsync(NetworkStream stream, Frame frame, CancellationToken ct)
    {
        byte[] body = Encoding.UTF8.GetBytes(frame.Body);
        int size = body.Length + 10;
        byte[] buffer = new byte[4 + size];
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0, 4), size);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4, 4), frame.Id);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(8, 4), frame.Type);
        body.CopyTo(buffer.AsSpan(12));
        await stream.WriteAsync(buffer, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken ct)
    {
        int filled = 0;
        while (filled < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(filled), ct).ConfigureAwait(false);
            if (n == 0)
            {
                return false; // EOF
            }

            filled += n;
        }

        return true;
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cts.Dispose();
    }
}
