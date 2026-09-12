using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;

namespace ZWarden.Domain.Operations;

/// <summary>
/// A durable, auditable unit of mutating work against a Server (<c>op-</c>), with its own lifecycle and
/// progress (CONTEXT.md). It moves through the closed state machine of ADR 0022 —
/// <c>Pending → Running → (Succeeded | Failed | Cancelled)</c>, with <see cref="OperationState.Cancelling"/>
/// the one transient — and that state, not the Agent-reported outcome, is the authoritative record.
/// <para>
/// Per-server serialization (PRD 21) is the Operation row itself: for a mutating Operation
/// (<see cref="IsMutating"/>), a partial unique index admits at most one in a non-terminal state per Server,
/// so acquiring the lock <b>is</b> inserting this row and releasing it <b>is</b> reaching a terminal state
/// (ADR 0005/0022). Read-only Operations (<see cref="IsMutating"/> <c>false</c>) sit outside the index and
/// never contend. This aggregate holds no I/O and no transaction; the lock lives in persistence.
/// </para>
/// It is <see cref="ITenantOwned"/> (stamped and filtered by the ambient tenant, ADR 0016) and
/// <see cref="IVersioned"/> (the portable optimistic token that guards every transition and drives the
/// reaper's takeover, ADR 0005).
/// </summary>
public sealed class Operation : IVersioned, ITenantOwned
{
    /// <summary>The greatest length stored for the Agent-supplied, untrusted <see cref="StatusLine"/> /
    /// <see cref="FailureReason"/>; longer text is truncated (trust-boundaries.md §3).</summary>
    public const int MaxReportedTextLength = 512;

    /// <summary>EF / factory use.</summary>
    public Operation()
    {
    }

    /// <summary>The Operation identifier (<c>op-&lt;uuid&gt;</c>).</summary>
    public OperationId Id { get; init; } = OperationId.New();

    /// <inheritdoc />
    public TenantId TenantId { get; init; }

    /// <summary>The Server this Operation targets — the per-server lock key.</summary>
    public ServerId ServerId { get; init; }

    /// <summary>What the Operation does. Stored by name.</summary>
    public OperationKind Kind { get; init; }

    /// <summary>Whether this Operation mutates the Server and so takes the per-server lock. Set at enqueue
    /// from the command's declared mutating-ness, not derived from <see cref="Kind"/> (ADR 0022), so the
    /// lock is testable before any mutating command exists. A read-only Operation never contends.</summary>
    public bool IsMutating { get; init; }

    /// <inheritdoc />
    public OperationState State { get; private set; } = OperationState.Pending;

    /// <summary>The caller-supplied idempotency key; a duplicate enqueue with the same
    /// <c>(TenantId, IdempotencyKey)</c> returns the existing Operation rather than creating a second
    /// (PRD 20). Never a secret.</summary>
    public string IdempotencyKey { get; init; } = string.Empty;

    /// <summary>Progress in <c>0..100</c>, advanced by <c>OperationProgress</c> and forced to 100 on
    /// success. Agent-reported and clamped.</summary>
    public int PercentComplete { get; private set; }

    /// <summary>The Agent's last progress note — <b>untrusted</b> display text: stored length-bounded, never
    /// interpreted, never used in a control-flow decision (trust-boundaries.md §3). <c>null</c> until the
    /// Agent reports one.</summary>
    public string? StatusLine { get; private set; }

    /// <summary>Why the Operation failed — the Agent's non-secret reason, or the reaper's lease-expiry
    /// reason. Untrusted, length-bounded. <c>null</c> unless <see cref="State"/> is
    /// <see cref="OperationState.Failed"/>.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>When the Operation was enqueued (UTC).</summary>
    public DateTimeOffset EnqueuedAt { get; init; }

    /// <summary>When the Operation was dispatched and began running (UTC); <c>null</c> until dispatched.</summary>
    public DateTimeOffset? StartedAt { get; private set; }

    /// <summary>When the Operation reached a terminal state (UTC); <c>null</c> until terminal.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>When the current lease expires (UTC). Set on dispatch, extended by each progress report,
    /// cleared on a terminal state. A <see cref="OperationState.Running"/>/<see cref="OperationState.Cancelling"/>
    /// Operation past this is reaped to <see cref="OperationState.Failed"/>, which frees the per-server lock
    /// (ADR 0022). <c>null</c> when not running.</summary>
    public DateTimeOffset? LeaseExpiresAt { get; private set; }

    /// <summary>When the Agent last reported progress (UTC); <c>null</c> until the first report.</summary>
    public DateTimeOffset? LastProgressAt { get; private set; }

    /// <inheritdoc />
    public Guid Version { get; set; }

    /// <summary>True once the Operation has reached a terminal, immutable state.</summary>
    public bool IsTerminal => State is OperationState.Succeeded or OperationState.Failed or OperationState.Cancelled;

    /// <summary>
    /// Creates a <see cref="OperationState.Pending"/> Operation. The <see cref="TenantId"/> is left unset so
    /// the ownership interceptor stamps the ambient tenant on insert (ADR 0016). <paramref name="isMutating"/>
    /// decides whether the per-server lock applies.
    /// </summary>
    public static Operation Enqueue(
        ServerId serverId,
        OperationKind kind,
        bool isMutating,
        string idempotencyKey,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        return new Operation
        {
            Id = OperationId.New(),
            ServerId = serverId,
            Kind = kind,
            IsMutating = isMutating,
            IdempotencyKey = idempotencyKey,
            State = OperationState.Pending,
            EnqueuedAt = now,
        };
    }

    /// <summary>Dispatches a <see cref="OperationState.Pending"/> Operation: it begins
    /// <see cref="OperationState.Running"/> under a lease. Legal only from <see cref="OperationState.Pending"/>.</summary>
    public void MarkDispatched(DateTimeOffset leaseExpiresAt, DateTimeOffset now)
    {
        if (State != OperationState.Pending)
        {
            throw new InvalidOperationStateTransitionException("dispatch", State);
        }

        State = OperationState.Running;
        StartedAt = now;
        LeaseExpiresAt = leaseExpiresAt;
        LastProgressAt = now;
    }

    /// <summary>Applies an Agent progress report, advancing <see cref="PercentComplete"/> (clamped to
    /// <c>0..100</c>), the untrusted <see cref="StatusLine"/> (truncated), and the lease. Does not change
    /// state. Legal while <see cref="OperationState.Running"/> or <see cref="OperationState.Cancelling"/> —
    /// an Agent may still report progress after a cancel is requested.</summary>
    public void ReportProgress(int percentComplete, string? statusLine, DateTimeOffset leaseExpiresAt, DateTimeOffset now)
    {
        if (State is not (OperationState.Running or OperationState.Cancelling))
        {
            throw new InvalidOperationStateTransitionException("report progress on", State);
        }

        PercentComplete = Math.Clamp(percentComplete, 0, 100);
        StatusLine = Truncate(statusLine);
        LeaseExpiresAt = leaseExpiresAt;
        LastProgressAt = now;
    }

    /// <summary>Records success (the Agent reported <c>OperationCompleted(Succeeded)</c>). Forces
    /// <see cref="PercentComplete"/> to 100 and clears the lease. Legal from
    /// <see cref="OperationState.Running"/> or <see cref="OperationState.Cancelling"/> (a cancel that lost the
    /// race still succeeded).</summary>
    public void Succeed(DateTimeOffset now)
    {
        if (State is not (OperationState.Running or OperationState.Cancelling))
        {
            throw new InvalidOperationStateTransitionException("succeed", State);
        }

        State = OperationState.Succeeded;
        PercentComplete = 100;
        CompletedAt = now;
        LeaseExpiresAt = null;
    }

    /// <summary>Records failure with a non-secret <paramref name="reason"/> (the Agent's reason, or the
    /// reaper's lease-expiry reason) and clears the lease, which frees the per-server lock. Legal from any
    /// non-terminal state — the reaper may fail a <see cref="OperationState.Pending"/> Operation whose
    /// enqueue window elapsed, and fails <see cref="OperationState.Running"/>/<see cref="OperationState.Cancelling"/>
    /// leases.</summary>
    public void Fail(string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (IsTerminal)
        {
            throw new InvalidOperationStateTransitionException("fail", State);
        }

        State = OperationState.Failed;
        FailureReason = Truncate(reason);
        CompletedAt = now;
        LeaseExpiresAt = null;
    }

    /// <summary>Requests cancellation. A <see cref="OperationState.Pending"/> Operation cancels immediately
    /// to <see cref="OperationState.Cancelled"/> (it was never dispatched); a <see cref="OperationState.Running"/>
    /// one enters <see cref="OperationState.Cancelling"/> (the cancel is then signalled to the Agent); a
    /// request while already <see cref="OperationState.Cancelling"/> is a no-op. Refused on a terminal
    /// Operation.</summary>
    public void RequestCancel(DateTimeOffset now)
    {
        switch (State)
        {
            case OperationState.Pending:
                State = OperationState.Cancelled;
                CompletedAt = now;
                LeaseExpiresAt = null;
                break;
            case OperationState.Running:
                State = OperationState.Cancelling;
                break;
            case OperationState.Cancelling:
                break;
            default:
                throw new InvalidOperationStateTransitionException("cancel", State);
        }
    }

    /// <summary>Completes a cancellation the Agent acknowledged: <see cref="OperationState.Cancelling"/> →
    /// <see cref="OperationState.Cancelled"/>, clearing the lease. Legal only from
    /// <see cref="OperationState.Cancelling"/>.</summary>
    public void Cancel(DateTimeOffset now)
    {
        if (State != OperationState.Cancelling)
        {
            throw new InvalidOperationStateTransitionException("complete cancellation of", State);
        }

        State = OperationState.Cancelled;
        CompletedAt = now;
        LeaseExpiresAt = null;
    }

    private static string? Truncate(string? text)
        => text is { Length: > MaxReportedTextLength } ? text[..MaxReportedTextLength] : text;
}
