using Microsoft.AspNetCore.SignalR.Client;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.ControlPlane;

/// <summary>
/// The live <see cref="IOperationProgressReporter"/> over the control-plane <see cref="HubConnection"/> (F17). It
/// sends an <see cref="OperationProgress"/> envelope (bound to the OperationId) on the
/// <see cref="AgentHubProtocol.OperationProgress"/> channel — the sibling of
/// <c>SendServerStateChangedAsync</c>/<c>SendMetricsReportAsync</c>. Built per dispatched command from the live
/// connection, so it is never a DI singleton (which would create a cycle with the connection).
/// </summary>
internal sealed class HubOperationProgressReporter : IOperationProgressReporter
{
    private readonly HubConnection _connection;
    private readonly TimeProvider _timeProvider;

    public HubOperationProgressReporter(HubConnection connection, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _connection = connection;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public Task ReportAsync(OperationId operationId, int percentComplete, string? statusLine, CancellationToken cancellationToken)
    {
        // A send while disconnected would throw; progress is best-effort and the operation's lease is the real
        // safety net, so drop it rather than fail the update over a transient disconnect.
        if (_connection.State != HubConnectionState.Connected)
        {
            return Task.CompletedTask;
        }

        Envelope<OperationProgress> envelope = Envelope.Create(
            new OperationProgress(percentComplete, statusLine), _timeProvider.GetUtcNow(), operationId: operationId);
        return _connection.SendAsync(AgentHubProtocol.OperationProgress, envelope, cancellationToken);
    }
}
