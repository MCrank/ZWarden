using ZWarden.Domain.Operations;

namespace ZWarden.Application.Operations;

/// <summary>
/// Sends an Operation's command to its Agent over the F10 connection. Implemented by ZWarden.Web (it owns
/// the SignalR hub and the connection registry); the engine core depends only on this seam, so it is
/// testable with a fake and a no-op default (F11 PR-A). The real implementation lands in PR-B.
/// <para>
/// The contract avoids the "completed before Running was persisted" race by persisting the
/// <see cref="OperationState.Running"/> transition <b>before</b> the command leaves: a successful dispatch
/// transitions the Operation to Running under a lease, saves that, then sends. If the Agent has no live
/// connection the Operation is left <see cref="OperationState.Pending"/>.
/// </para>
/// </summary>
public interface IOperationDispatcher
{
    /// <summary>
    /// Attempts to dispatch a <see cref="OperationState.Pending"/> Operation to
    /// <see cref="Operation.AgentId"/>. On a live connection: transitions it to
    /// <see cref="OperationState.Running"/> under a lease, persists that, sends the command, and returns
    /// <c>true</c>. With no live connection: leaves it <see cref="OperationState.Pending"/> and returns
    /// <c>false</c>.
    /// </summary>
    Task<bool> TryDispatchAsync(Operation operation, CancellationToken cancellationToken = default);
}
