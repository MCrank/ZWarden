namespace ZWarden.Domain.Operations;

/// <summary>
/// The lifecycle state of an <see cref="Operation"/> (ADR 0022). The transitions are the whole
/// vocabulary — <c>Pending → Running → (Succeeded | Failed | Cancelled)</c> with <see cref="Cancelling"/>
/// the one transient — and nothing else is legal. This state, not the Agent-reported
/// <c>OperationOutcome</c>, is the durable record five downstream features build on.
/// </summary>
public enum OperationState
{
    /// <summary>Enqueued and awaiting dispatch. For a mutating Operation this is already holding the
    /// per-server lock (ADR 0005/0022). Cancelling a <see cref="Pending"/> Operation goes straight to
    /// <see cref="Cancelled"/> — it was never dispatched.</summary>
    Pending = 0,

    /// <summary>Dispatched to the Agent and running under a lease. Advanced by <c>OperationProgress</c>;
    /// ended by <c>OperationCompleted</c> or, if the lease expires, by the reaper.</summary>
    Running = 1,

    /// <summary>Cancellation has been requested for a <see cref="Running"/> Operation and signalled to the
    /// Agent; it reaches <see cref="Cancelled"/> on acknowledgement or <see cref="Failed"/> if the lease
    /// expires first. The one transient state.</summary>
    Cancelling = 2,

    /// <summary>Terminal — the Agent reported success. Immutable.</summary>
    Succeeded = 3,

    /// <summary>Terminal — the Agent reported failure, or the reaper failed a lease-expired Operation.
    /// Carries a non-secret reason. Immutable.</summary>
    Failed = 4,

    /// <summary>Terminal — cancelled before dispatch, or a running cancellation the Agent acknowledged.
    /// Immutable.</summary>
    Cancelled = 5,
}
