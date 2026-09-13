using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

namespace ZWarden.Rcon;

/// <summary>
/// One authenticated Source RCON connection to a single PZ server: a single long-lived TCP socket
/// with commands serialized one at a time (F18 decision D-3). This shape is dictated by two PZ
/// traps - the server caps simultaneous connections at five and accepts-then-instantly-closes the
/// sixth (research §7 quirk 7), and it executes every command on the main tick with no pipelining
/// (quirk 8) - so a pool of one, guarded by a gate, is both correct and safe. The socket is kept
/// alive across commands (PZ never idle-closes it, quirk 5) and reconnected lazily only when it is
/// found dead <b>before</b> a command is sent, so a command is never silently run twice.
/// </summary>
public sealed class RconConnection : IAsyncDisposable
{
    private readonly RconEndpoint _endpoint;
    private readonly RconOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private TcpClient? _client;
    private NetworkStream? _stream;
    private int _nextId;
    private bool _disposed;

    public RconConnection(RconEndpoint endpoint, RconOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint.Host);
        _endpoint = endpoint;
        _options = options ?? RconOptions.Default;
    }

    /// <summary>True once a socket is open and authenticated. Cleared on any fault or disposal.</summary>
    public bool IsConnected => _stream is not null;

    /// <summary>
    /// Opens the socket and authenticates now, rather than lazily on the first command. Useful for a
    /// health probe that wants to distinguish "cannot reach / authenticate" from "command failed".
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Sends one admin command and returns its full response text. An <b>empty</b> result is a
    /// legitimate outcome, not a fault: PZ sends no packet at all for a command that produces no
    /// output (research §7 quirk 3), so this returns <see cref="string.Empty"/> once the idle window
    /// passes with nothing received - it never blocks waiting for a terminator PZ will not send.
    /// The command text is sent as-is; quoting and construction are the caller's responsibility
    /// (F19/F28), and only ASCII is portable (quirk 11).
    /// </summary>
    /// <exception cref="RconAuthenticationException">The password was rejected.</exception>
    /// <exception cref="RconTimeoutException">The exchange did not finish within
    /// <see cref="RconOptions.CommandTimeout"/>.</exception>
    /// <exception cref="RconException">The connection was lost while reading the response (the
    /// command may or may not have run - it is not retried, to avoid a double execution).</exception>
    public async Task<string> ExecuteAsync(string command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var deadline = CreateDeadline(_options.CommandTimeout, cancellationToken);
            var packet = new RconPacket(NextId(), RconConstants.Type.ExecCommand, command);

            // Send, reconnecting once if the socket turns out to be stale. A failed *write* means the
            // command never reached the server, so re-sending is safe; a failed *read* (below) is not.
            await SendWithStaleReconnectAsync(packet, cancellationToken, deadline.Token).ConfigureAwait(false);

            try
            {
                return await ReadCommandResponseAsync(cancellationToken, deadline.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or SocketException or EndOfStreamException)
            {
                DropConnection();
                throw new RconException(
                    "The RCON connection was lost while reading the command response.", ex);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    // --- connection lifecycle -------------------------------------------------------------------

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_stream is not null)
        {
            return;
        }

        using var deadline = CreateDeadline(_options.ConnectTimeout, cancellationToken);
        var client = new TcpClient { NoDelay = true };
        bool established = false;
        try
        {
            await client.ConnectAsync(_endpoint.Host, _endpoint.Port, deadline.Token).ConfigureAwait(false);
            _client = client;
            _stream = client.GetStream();
            await AuthenticateAsync(cancellationToken, deadline.Token).ConfigureAwait(false);
            established = true;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new RconTimeoutException(
                $"Timed out connecting to RCON at {_endpoint} within {_options.ConnectTimeout}.");
        }
        catch (Exception ex) when (ex is SocketException or IOException or EndOfStreamException)
        {
            // A transport-level failure at connect or handshake - unreachable, RCON disabled, or the
            // five-connection cap reached (the sixth socket is accepted then instantly closed). Surface
            // it as an RconException so callers see one connection-fault type, not a raw socket error.
            // RconAuthenticationException (a wrong password) is thrown explicitly and passes through.
            throw new RconException(
                $"Could not establish an RCON connection to {_endpoint} (the server may be unreachable, " +
                "RCON disabled, or the five-connection cap reached).", ex);
        }
        finally
        {
            if (!established)
            {
                DropConnection();
                client.Dispose();
            }
        }
    }

    private async Task AuthenticateAsync(CancellationToken userToken, CancellationToken deadlineToken)
    {
        int requestId = NextId();
        var auth = new RconPacket(requestId, RconConstants.Type.Auth, _endpoint.Password.HasValue
            ? _endpoint.Password.Reveal()
            : string.Empty);
        await WritePacketAsync(auth, deadlineToken).ConfigureAwait(false);

        // PZ replies with an (ignored) empty RESPONSE_VALUE packet, then an AUTH_RESPONSE whose id is
        // the request id on success or -1 on failure - after which, on failure, it closes the socket.
        while (true)
        {
            RconPacket reply;
            try
            {
                reply = await ReadFrameStrictAsync(userToken, deadlineToken).ConfigureAwait(false);
            }
            catch (EndOfStreamException)
            {
                throw new RconAuthenticationException(
                    $"The RCON server at {_endpoint} closed the connection during authentication " +
                    "(an empty password disables RCON, or the password was rejected).");
            }

            if (reply.Type != RconConstants.Type.AuthResponse)
            {
                continue; // the leading empty RESPONSE_VALUE packet
            }

            if (reply.Id == RconConstants.AuthFailureId)
            {
                throw new RconAuthenticationException(
                    $"The RCON password was rejected by the server at {_endpoint}.");
            }

            if (reply.Id != requestId)
            {
                throw new RconProtocolException(
                    $"RCON auth response carried id {reply.Id}, expected {requestId}.");
            }

            return; // authenticated
        }
    }

    private async Task SendWithStaleReconnectAsync(
        RconPacket packet, CancellationToken userToken, CancellationToken deadlineToken)
    {
        await EnsureConnectedAsync(userToken).ConfigureAwait(false);
        try
        {
            await WritePacketAsync(packet, deadlineToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException)
        {
            // The socket was stale; the command did not reach the server. Reconnect and re-send once.
            DropConnection();
            await EnsureConnectedAsync(userToken).ConfigureAwait(false);
            await WritePacketAsync(packet, deadlineToken).ConfigureAwait(false);
        }
    }

    private void DropConnection()
    {
        _stream?.Dispose();
        _client?.Dispose();
        _stream = null;
        _client = null;
    }

    // --- reading --------------------------------------------------------------------------------

    /// <summary>
    /// Reassembles a command's response from its chunks. The response is complete when a chunk shorter
    /// than PZ's 4086-byte split arrives (the only positive end signal PZ gives), or when the idle
    /// window passes with no further bytes - which is also how an empty result (no packet at all)
    /// resolves to an empty string. Total size is bounded to guard against untrusted runtime output.
    /// </summary>
    private async Task<string> ReadCommandResponseAsync(CancellationToken userToken, CancellationToken deadlineToken)
    {
        var builder = new StringBuilder();
        int totalBytes = 0;
        bool sawEof = false;

        while (true)
        {
            RconPacket? frame;
            try
            {
                frame = await TryReadFrameWithinIdleAsync(userToken, deadlineToken).ConfigureAwait(false);
            }
            catch (EndOfStreamException)
            {
                // PZ closed the socket. Whatever we have is all we will get; the connection is dead.
                sawEof = true;
                break;
            }

            if (frame is null)
            {
                break; // idle: no more data within the window => response complete (empty if nothing seen)
            }

            RconPacket packet = frame.Value;
            totalBytes += packet.BodyByteCount;
            if (totalBytes > _options.MaxResponseBytes)
            {
                DropConnection();
                throw new RconProtocolException(
                    $"RCON response exceeded the {_options.MaxResponseBytes}-byte cap; connection dropped.");
            }

            builder.Append(packet.Body);

            if (packet.BodyByteCount < RconConstants.MaxResponseChunkBodyBytes)
            {
                break; // a short chunk is the last chunk
            }
        }

        if (sawEof)
        {
            DropConnection();
        }

        return builder.ToString();
    }

    /// <summary>
    /// Reads one frame, treating the very first byte as idle-sensitive: if nothing arrives within the
    /// idle window it returns <c>null</c> (no frame - the response is complete or empty). Once any
    /// byte of a frame has arrived, the rest is read under the overall deadline only, so a TCP segment
    /// boundary inside a frame never gets mistaken for the end of the response.
    /// </summary>
    private async Task<RconPacket?> TryReadFrameWithinIdleAsync(CancellationToken userToken, CancellationToken deadlineToken)
    {
        var lengthPrefix = new byte[4];
        int filled = 0;
        while (filled < 4)
        {
            if (filled == 0)
            {
                int n = await ReadOnceWithinIdleAsync(lengthPrefix.AsMemory(0, 4), userToken, deadlineToken)
                    .ConfigureAwait(false);
                if (n == 0)
                {
                    return null; // idle window elapsed before any byte => no frame
                }

                filled += n;
            }
            else
            {
                filled += await ReadOnceStrictAsync(lengthPrefix.AsMemory(filled, 4 - filled), userToken, deadlineToken)
                    .ConfigureAwait(false);
            }
        }

        int size = BinaryPrimitives.ReadInt32LittleEndian(lengthPrefix);
        ValidateSizeField(size);

        var payload = new byte[size];
        await ReadExactAsync(payload, userToken, deadlineToken).ConfigureAwait(false);
        return RconPacket.DecodePayload(payload);
    }

    private async Task<RconPacket> ReadFrameStrictAsync(CancellationToken userToken, CancellationToken deadlineToken)
    {
        var lengthPrefix = new byte[4];
        await ReadExactAsync(lengthPrefix, userToken, deadlineToken).ConfigureAwait(false);

        int size = BinaryPrimitives.ReadInt32LittleEndian(lengthPrefix);
        ValidateSizeField(size);

        var payload = new byte[size];
        await ReadExactAsync(payload, userToken, deadlineToken).ConfigureAwait(false);
        return RconPacket.DecodePayload(payload);
    }

    private static void ValidateSizeField(int size)
    {
        // Guard against PZ's own unhardened framing (quirk 12) being reflected at us, and against a
        // hostile size prefix: reject anything outside the legal range before allocating a buffer.
        if (size < RconConstants.MinSizeField || size > RconConstants.MaxSizeField)
        {
            throw new RconProtocolException(
                $"RCON frame declared an out-of-range size {size} " +
                $"(legal {RconConstants.MinSizeField}..{RconConstants.MaxSizeField}).");
        }
    }

    private async Task ReadExactAsync(Memory<byte> buffer, CancellationToken userToken, CancellationToken deadlineToken)
    {
        int filled = 0;
        while (filled < buffer.Length)
        {
            filled += await ReadOnceStrictAsync(buffer[filled..], userToken, deadlineToken).ConfigureAwait(false);
        }
    }

    /// <summary>One read that must yield data: EOF is an <see cref="EndOfStreamException"/>, a fired
    /// deadline an <see cref="RconTimeoutException"/>, and caller cancellation propagates.</summary>
    private async Task<int> ReadOnceStrictAsync(Memory<byte> buffer, CancellationToken userToken, CancellationToken deadlineToken)
    {
        NetworkStream stream = _stream ?? throw new EndOfStreamException("The RCON connection is not open.");
        int n;
        try
        {
            n = await stream.ReadAsync(buffer, deadlineToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (deadlineToken.IsCancellationRequested && !userToken.IsCancellationRequested)
        {
            throw new RconTimeoutException($"Timed out reading from RCON at {_endpoint}.");
        }

        if (n == 0)
        {
            throw new EndOfStreamException("The RCON server closed the connection.");
        }

        return n;
    }

    /// <summary>One read whose first-byte wait is bounded by the idle window: returns 0 when the
    /// window elapses with no data (a signal, not an error), otherwise the bytes read.</summary>
    private async Task<int> ReadOnceWithinIdleAsync(Memory<byte> buffer, CancellationToken userToken, CancellationToken deadlineToken)
    {
        NetworkStream stream = _stream ?? throw new EndOfStreamException("The RCON connection is not open.");
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(deadlineToken);
        idle.CancelAfter(_options.IdleWindow);

        int n;
        try
        {
            n = await stream.ReadAsync(buffer, idle.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (userToken.IsCancellationRequested)
            {
                throw;
            }

            if (deadlineToken.IsCancellationRequested)
            {
                throw new RconTimeoutException($"Timed out reading from RCON at {_endpoint}.");
            }

            return 0; // idle window elapsed: no data available => no frame
        }

        if (n == 0)
        {
            throw new EndOfStreamException("The RCON server closed the connection.");
        }

        return n;
    }

    // --- writing --------------------------------------------------------------------------------

    private async Task WritePacketAsync(RconPacket packet, CancellationToken deadlineToken)
    {
        NetworkStream stream = _stream ?? throw new IOException("The RCON connection is not open.");
        byte[] frame = packet.Encode();
        await stream.WriteAsync(frame, deadlineToken).ConfigureAwait(false);
        await stream.FlushAsync(deadlineToken).ConfigureAwait(false);
    }

    // --- helpers --------------------------------------------------------------------------------

    private int NextId()
    {
        // Serialized under _gate, but keep it monotonic and never the reserved -1.
        int id = ++_nextId;
        if (id == RconConstants.AuthFailureId || id == 0)
        {
            id = _nextId = 1;
        }

        return id;
    }

    private static CancellationTokenSource CreateDeadline(TimeSpan timeout, CancellationToken userToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(userToken);
        cts.CancelAfter(timeout);
        return cts;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DropConnection();
        _gate.Dispose();
        await ValueTask.CompletedTask.ConfigureAwait(false);
    }
}
