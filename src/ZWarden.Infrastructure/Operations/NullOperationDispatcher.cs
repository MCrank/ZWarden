using ZWarden.Application.Operations;
using ZWarden.Domain.Operations;

namespace ZWarden.Infrastructure.Operations;

/// <summary>
/// The default <see cref="IOperationDispatcher"/> when no transport is wired (F11 PR-A, and any host without
/// the Agent control plane). It dispatches nothing and reports the Agent as unreachable, so an enqueued
/// Operation stays <see cref="OperationState.Pending"/>. ZWarden.Web replaces it in PR-B with the real
/// SignalR dispatcher; it is registered with <c>TryAdd</c> so that replacement wins.
/// </summary>
public sealed class NullOperationDispatcher : IOperationDispatcher
{
    /// <inheritdoc />
    public Task<bool> TryDispatchAsync(Operation operation, CancellationToken cancellationToken = default)
        => Task.FromResult(false);
}
