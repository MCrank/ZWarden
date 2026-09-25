namespace ZWarden.Agent.Docker;

/// <summary>
/// An operator-requested host port pair cannot be used (#229): it is out of the allowed range, or a port of the pair
/// is already published on the host. The message is operator-facing and actionable.
/// </summary>
public sealed class PortUnavailableException : Exception
{
    /// <summary>Creates the exception with an operator-facing message.</summary>
    public PortUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with an operator-facing message and the underlying cause.</summary>
    public PortUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception with a generic message.</summary>
    public PortUnavailableException()
        : base("The requested host ports are not available.")
    {
    }
}
