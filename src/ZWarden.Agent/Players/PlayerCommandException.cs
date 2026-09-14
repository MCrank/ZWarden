namespace ZWarden.Agent.Players;

/// <summary>
/// A player-management command could not be run against a Server's RCON (F19): the arguments failed validation
/// (D-3), RCON is disabled or unreachable, the password was rejected, or the command timed out. The
/// <see cref="Exception.Message"/> is an <b>Agent-authored</b>, non-secret reason safe to surface to the
/// operator (never PZ's untrusted output, never the password); the command processor maps it to a failed
/// <c>OperationCompleted</c>, mirroring how the lifecycle commands map their container faults.
/// </summary>
public sealed class PlayerCommandException : Exception
{
    public PlayerCommandException(string message)
        : base(message)
    {
    }
}
