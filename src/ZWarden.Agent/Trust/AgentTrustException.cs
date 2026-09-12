namespace ZWarden.Agent.Trust;

/// <summary>
/// Raised when the Agent's trust file exists but cannot be read as valid trust material. Like the identity
/// store (F8), the Agent <b>fails typed rather than silently minting or discarding trust</b> — a malformed
/// file is surfaced, never overwritten, so an operator can resolve it instead of the Agent quietly losing
/// its enrolment.
/// </summary>
public sealed class AgentTrustException : Exception
{
    public AgentTrustException(string message)
        : base(message)
    {
    }

    public AgentTrustException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
