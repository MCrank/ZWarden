namespace ZWarden.Agent.Docker;

/// <summary>
/// Thrown when creating a canonical container fails in a way the operator should see. It carries a classified
/// <see cref="ContainerCreateFailure"/> and an Agent-authored, actionable message — in particular, the
/// pre-provision case (ADR 0008 §3.4) is surfaced as guidance to pull the image, not as an opaque 404.
/// </summary>
public sealed class ContainerCreateException : Exception
{
    /// <summary>Creates the exception with a classified failure and an actionable message.</summary>
    public ContainerCreateException(ContainerCreateFailure failure, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
    }

    /// <summary>The classified failure.</summary>
    public ContainerCreateFailure Failure { get; }
}
