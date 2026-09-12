using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;

namespace ZWarden.Application.Operations;

/// <summary>
/// The engine's enqueue and cancellation surface (F11). It creates an <see cref="Operation"/>, acquires the
/// per-server lock (PRD 21) as the same insert, enforces enqueue idempotency (PRD 20), attempts an initial
/// dispatch through <see cref="IOperationDispatcher"/>, and audits each transition (F6). It does <b>not</b>
/// authorize — the calling feature does that under its own permission — and it holds no transaction across
/// Agent work (ADR 0005 condition 5). Progress and completion arrive separately through
/// <see cref="IOperationStore"/>.
/// </summary>
public interface IOperationCoordinator
{
    /// <summary>
    /// Enqueues an Operation and attempts to dispatch it. Returns the created Operation, or the existing one
    /// when <see cref="EnqueueOperationRequest.IdempotencyKey"/> already names one in this tenant (PRD 20).
    /// </summary>
    /// <exception cref="ServerBusyException">A conflicting mutating Operation is already in flight against
    /// the same Server (PRD 21).</exception>
    Task<Operation> EnqueueAsync(
        EnqueueOperationRequest request,
        UserId? actor = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests cancellation of an Operation: a <see cref="OperationState.Pending"/> one cancels immediately,
    /// a <see cref="OperationState.Running"/> one enters <see cref="OperationState.Cancelling"/> and the
    /// cancel is signalled to the Agent (cooperative). Returns the updated Operation.
    /// </summary>
    /// <exception cref="OperationNotFoundException">No such Operation in the current tenant.</exception>
    Task<Operation> RequestCancellationAsync(
        OperationId operationId,
        UserId? actor = null,
        CancellationToken cancellationToken = default);
}
