namespace ZWarden.Agent.Console;

/// <summary>
/// A remote-console command could not be run against a Server's RCON (F28): the input failed validation or the
/// command was denied by the F28 policy (ADR 0032), RCON is disabled or unreachable, the password was rejected,
/// or the command timed out. The <see cref="Exception.Message"/> is an <b>Agent-authored</b>, non-secret reason
/// safe to surface to the operator (never PZ's untrusted output, never the password); the command processor maps
/// it to a failed <c>OperationCompleted</c>, mirroring how the player commands map their RCON faults.
/// </summary>
public sealed class ConsoleCommandException : Exception
{
    public ConsoleCommandException(string message)
        : base(message)
    {
    }
}
