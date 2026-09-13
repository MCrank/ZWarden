namespace ZWarden.Rcon;

/// <summary>
/// Base for every fault the RCON client raises. Distinct from a plain <see cref="System.IO.IOException"/>
/// so a caller can tell "the RCON exchange went wrong" from "the socket died", and so the Agent's
/// health probe can classify the failure legibly rather than surfacing an opaque socket error.
/// </summary>
public class RconException : Exception
{
    public RconException(string message) : base(message) { }
    public RconException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Authentication was rejected: PZ replied with the auth-failure id (<c>-1</c>) and closed the
/// socket (research §7 quirk 6). The password did not match - retrying with the same password only
/// burns one of PZ's five connection slots, so the client fails fast rather than looping.
/// </summary>
public sealed class RconAuthenticationException : RconException
{
    public RconAuthenticationException(string message) : base(message) { }
}

/// <summary>
/// A command did not complete within its deadline. This is a genuine timeout - the server never
/// finished replying - and is <b>not</b> the same as an empty result, which PZ signals by sending
/// no packet at all and the client resolves to an empty string within the idle window (research §7
/// quirks 2-3).
/// </summary>
public sealed class RconTimeoutException : RconException
{
    public RconTimeoutException(string message) : base(message) { }
}

/// <summary>
/// The bytes on the wire did not form a legal Source RCON frame - a size prefix out of range, a
/// truncated payload, or an unexpected packet type in an exchange. Everything the PZ runtime emits
/// is untrusted (trust-boundaries §8), so malformed framing is rejected, never guessed at.
/// </summary>
public sealed class RconProtocolException : RconException
{
    public RconProtocolException(string message) : base(message) { }
}
