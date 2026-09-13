namespace ZWarden.Rcon;

/// <summary>
/// The timing and safety knobs the client needs to survive PZ's RCON traps. The defaults are the
/// values the F18 mini-plan locked (decision D-1): they assume a command costs at least one server
/// tick plus up to 50 ms of poll latency (research §7 quirk 8), and that "no bytes for a short
/// window" is the only way to tell a complete short response - or an empty result - from more chunks
/// still in flight.
/// </summary>
public sealed record RconOptions
{
    /// <summary>
    /// How long the client waits for more response bytes after the last it received (or after the
    /// command was sent, for an empty result) before deciding the response is complete. PZ emits no
    /// end-of-response sentinel and sends nothing at all for an empty result (research §7 quirks
    /// 1-3), so this window - not a terminator - closes a read. Kept short so a real reply is not
    /// delayed, but longer than a tick so a slow single-chunk reply is not cut off.
    /// </summary>
    public TimeSpan IdleWindow { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>The hard ceiling on one command's whole request/response exchange. Exceeding it
    /// raises <see cref="RconTimeoutException"/> - a genuine "the server never finished", distinct
    /// from an empty result.</summary>
    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How long to wait for the TCP connect and the auth handshake to complete.</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// The absolute cap on bytes accumulated for a single response, across all chunks. A defensive
    /// bound on untrusted PZ output (trust-boundaries §8): a compromised or buggy runtime cannot make
    /// the client buffer without limit. Generous enough for the largest real admin reply (a full
    /// <c>help</c> or <c>showoptions</c> spanning many chunks).
    /// </summary>
    public int MaxResponseBytes { get; init; } = 1024 * 1024;

    /// <summary>A shared default instance.</summary>
    public static RconOptions Default { get; } = new();
}
