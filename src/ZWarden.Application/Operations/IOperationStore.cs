using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;

namespace ZWarden.Application.Operations;

/// <summary>
/// The tenant-scoped read and ingest surface for Operations (F11). Reads go through the tenant filter
/// (ADR 0016) — never <c>IgnoreQueryFilters</c>. The ingest methods apply Agent-reported events
/// (<c>OperationProgress</c>/<c>OperationCompleted</c>) that arrive over the hub with no browser session;
/// they resolve the Operation by id, apply the transition, extend or clear the lease, and audit the
/// terminal ones. Agent-supplied text is untrusted (trust-boundaries.md §3): stored length-bounded, never
/// interpreted.
/// </summary>
public interface IOperationStore
{
    /// <summary>Finds an Operation by id in the current tenant, or <c>null</c> if not visible.</summary>
    Task<Operation?> FindAsync(OperationId operationId, CancellationToken cancellationToken = default);

    /// <summary>Applies an Agent progress report: advances the (clamped) percentage and (truncated) status
    /// line and extends the lease. No-op if the Operation is not visible or is already terminal.</summary>
    Task ApplyProgressAsync(
        OperationId operationId,
        int percentComplete,
        string? statusLine,
        CancellationToken cancellationToken = default);

    /// <summary>Records that the Agent reported success. Drives the Operation to
    /// <see cref="OperationState.Succeeded"/> and audits it. No-op if not visible or already terminal.</summary>
    Task CompleteSucceededAsync(OperationId operationId, CancellationToken cancellationToken = default);

    /// <summary>Records that the Agent reported failure with a non-secret reason. Drives the Operation to
    /// <see cref="OperationState.Failed"/> and audits it. No-op if not visible or already terminal.</summary>
    Task CompleteFailedAsync(
        OperationId operationId,
        string? failureReason,
        CancellationToken cancellationToken = default);
}
