namespace ZWarden.Domain.Operations;

/// <summary>
/// Thrown when an <see cref="Operation"/> is asked to make a transition its current
/// <see cref="OperationState"/> does not permit — e.g. dispatching a non-<c>Pending</c> Operation, or
/// cancelling a terminal one. The state machine (ADR 0022) is closed: an illegal transition is a
/// programming error, not a runtime condition to recover from.
/// </summary>
public sealed class InvalidOperationStateTransitionException : InvalidOperationException
{
    /// <summary>The state the Operation was in when the illegal transition was attempted.</summary>
    public OperationState From { get; }

    /// <summary>A short name for the transition that was refused (e.g. <c>"dispatch"</c>).</summary>
    public string Transition { get; }

    /// <summary>Creates the exception for <paramref name="transition"/> refused from state
    /// <paramref name="from"/>.</summary>
    public InvalidOperationStateTransitionException(string transition, OperationState from)
        : base($"Cannot {transition} an operation in state {from}.")
    {
        Transition = transition;
        From = from;
    }
}
