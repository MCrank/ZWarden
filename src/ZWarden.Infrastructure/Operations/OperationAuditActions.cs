namespace ZWarden.Infrastructure.Operations;

/// <summary>
/// The stable, machine-readable audit action names for the operations engine (F11; F6, ADR 0019). Every
/// lifecycle transition is recorded, so an operator reconstructs an Operation's whole life in the viewer.
/// No secret is ever recorded under any of these; Agent-supplied text (a status line, a failure reason) is
/// non-secret but untrusted and length-bounded.
/// </summary>
public static class OperationAuditActions
{
    /// <summary>An Operation was enqueued (and, for a mutating one, took the per-server lock).</summary>
    public const string Enqueued = "Operation.Enqueued";

    /// <summary>An Operation was dispatched to its Agent and began running.</summary>
    public const string Started = "Operation.Started";

    /// <summary>An Operation completed successfully.</summary>
    public const string Succeeded = "Operation.Succeeded";

    /// <summary>An Operation failed — the Agent reported failure, or the reaper failed an expired lease.</summary>
    public const string Failed = "Operation.Failed";

    /// <summary>An Operation was cancelled — before dispatch, or a running cancellation the Agent honoured.</summary>
    public const string Cancelled = "Operation.Cancelled";
}
