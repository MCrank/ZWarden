namespace ZWarden.Agent.Docker;

/// <summary>
/// Thrown when a target-container operation is asked to act on a container this Agent does not own — one that
/// is not a canonical ZWarden container, or is stamped with a different Agent's id. This is the failure of the
/// label-and-assignment check that "deserves the same test rigour as an authorization check"
/// (trust-boundaries.md §4): a bug that resolves one container too many must <b>refuse</b>, never act. It is
/// raised <i>before</i> any Docker verb is issued, so a mis-resolved container is inspected and rejected, not
/// mutated.
/// </summary>
public sealed class ForeignContainerException : Exception
{
    /// <summary>Creates the exception for a refused container.</summary>
    /// <param name="containerId">The Docker id of the container that was refused.</param>
    /// <param name="reason">Why it was refused (Agent-authored, not daemon output).</param>
    public ForeignContainerException(string containerId, string reason)
        : base($"Refused to operate on container '{containerId}': {reason}.")
    {
        ContainerId = containerId;
        Reason = reason;
    }

    /// <summary>The Docker id of the container that was refused.</summary>
    public string ContainerId { get; }

    /// <summary>Why the container was refused.</summary>
    public string Reason { get; }
}
