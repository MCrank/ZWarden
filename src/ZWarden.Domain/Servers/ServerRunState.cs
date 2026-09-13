namespace ZWarden.Domain.Servers;

/// <summary>
/// The coarse <b>last-reported</b> run-state of a <see cref="Server"/> as ZWarden.Web last recorded it
/// from an Agent state snapshot — observed, never inferred (trust-boundaries.md §3). This is the Domain's
/// own vocabulary: the wire <c>ZWarden.Contracts.Protocol.ServerRunState</c> cannot be referenced here
/// (the Domain references no Contracts), so the reconciler in ZWarden.Web maps wire → this. Deliberately
/// minimal and mirrors the wire's coarse states; F16 layers the hierarchical health model on top. Stored
/// by name, so a later addition is additive, never a silent renumber.
/// </summary>
public enum ServerRunState
{
    /// <summary>No confirmed state has been reported for this Server yet.</summary>
    Unknown = 0,

    /// <summary>Not running.</summary>
    Stopped = 1,

    /// <summary>Starting up; not yet accepting players.</summary>
    Starting = 2,

    /// <summary>Running.</summary>
    Running = 3,

    /// <summary>Shutting down.</summary>
    Stopping = 4,

    /// <summary>Exited abnormally or failed to start.</summary>
    Failed = 5,
}
