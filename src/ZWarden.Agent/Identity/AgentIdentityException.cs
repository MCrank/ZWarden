namespace ZWarden.Agent.Identity;

/// <summary>
/// Thrown when the Agent's persisted identity cannot be read as a valid identity (F8). It is a
/// typed, actionable failure — never silently replaced with a fresh id, which would orphan the
/// Agent's later enrollment (F9).
/// </summary>
public sealed class AgentIdentityException : Exception
{
    /// <summary>Creates the exception with an actionable message.</summary>
    public AgentIdentityException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with an actionable message and an underlying cause.</summary>
    public AgentIdentityException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
