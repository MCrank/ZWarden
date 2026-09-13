using System.Collections.Concurrent;
using ZWarden.Agent.Docker;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.ControlPlane;

/// <summary>
/// Handles an <see cref="AgentCommand"/> the control plane dispatches (F11), deciding the reply to send back.
/// It deserializes the canonical wire envelope, dispatches on the command type, and <b>dedupes by
/// <c>OperationId</c></b> so a redelivered command (a reconnect replay) runs the work once (PRD 20). F11 added
/// <see cref="PingAgent"/> (an immediate round-trip); F13 adds <see cref="ProbeDockerHealth"/> (a non-mutating
/// Docker connectivity probe). The real mutating commands land with their owning features. Dispatch is
/// transport-free, so it is unit tested without a live connection; the connection wires it to the
/// <see cref="AgentHubProtocol.ReceiveCommand"/> channel.
/// </summary>
public sealed class AgentCommandProcessor
{
    private readonly TimeProvider _timeProvider;
    private readonly IContainerRuntime _containerRuntime;
    private readonly ConcurrentDictionary<OperationId, byte> _handled = new();

    public AgentCommandProcessor(TimeProvider timeProvider, IContainerRuntime containerRuntime)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(containerRuntime);
        _timeProvider = timeProvider;
        _containerRuntime = containerRuntime;
    }

    /// <summary>
    /// Processes a dispatched command's canonical wire JSON and returns the terminal
    /// <see cref="OperationCompleted"/> envelope to send back, or <c>null</c> when there is nothing to send —
    /// a replay of an already-handled operation, a command carrying no <c>OperationId</c>, or a command this
    /// Agent version does not handle.
    /// </summary>
    public async Task<Envelope<OperationCompleted>?> ProcessAsync(string commandJson, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commandJson);

        Envelope<IProtocolMessage> envelope = ProtocolJson.Deserialize(commandJson);
        if (envelope.OperationId is not { } operationId)
        {
            return null;
        }

        switch (envelope.Payload)
        {
            case PingAgent:
                if (!_handled.TryAdd(operationId, 0))
                {
                    return null; // Already handled this operation — a redelivered command (PRD 20).
                }

                return Completed(OperationOutcome.Succeeded, failureReason: null, operationId);

            case ProbeDockerHealth:
                if (!_handled.TryAdd(operationId, 0))
                {
                    return null; // Already handled this operation — a redelivered command (PRD 20).
                }

                DockerHealth health = await _containerRuntime.ProbeHealthAsync(cancellationToken).ConfigureAwait(false);
                return health.DaemonReachable
                    ? Completed(OperationOutcome.Succeeded, failureReason: null, operationId)
                    : Completed(OperationOutcome.Failed, health.Detail, operationId);

            default:
                // A command this Agent version does not understand: leave it unhandled (not marked handled) so
                // a future version can process a redelivery. The operation's lease reaps it if never handled.
                return null;
        }
    }

    private Envelope<OperationCompleted> Completed(OperationOutcome outcome, string? failureReason, OperationId operationId) =>
        Envelope.Create(
            new OperationCompleted(outcome, failureReason),
            _timeProvider.GetUtcNow(),
            operationId: operationId);
}
