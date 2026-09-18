namespace ZWarden.Agent.Servers;

/// <summary>
/// A server-wide broadcast (<c>servermsg</c>) could not be assembled or sent (#114): the message failed
/// validation, or the Server's RCON is disabled/unreachable. The <see cref="Exception.Message"/> is an
/// <b>Agent-authored</b>, non-secret reason (never PZ's untrusted output, never the password). Unlike a
/// lifecycle fault, a broadcast failure is <b>best-effort</b> — the graceful-restart coordinator catches it,
/// logs it, and proceeds with the restart regardless (the warning is a courtesy, not a gate).
/// </summary>
public sealed class BroadcastCommandException : Exception
{
    public BroadcastCommandException(string message)
        : base(message)
    {
    }
}
