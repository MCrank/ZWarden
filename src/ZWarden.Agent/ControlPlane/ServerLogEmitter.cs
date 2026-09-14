using Microsoft.AspNetCore.SignalR.Client;
using ZWarden.Agent.LogStreaming;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.ControlPlane;

/// <summary>
/// The live <see cref="IServerLogEmitter"/> over the control-plane <see cref="HubConnection"/> (F27). It sends a
/// <see cref="ServerLogBatch"/> envelope (bound to the Server) on the <see cref="AgentHubProtocol.ServerLogBatch"/>
/// channel — the sibling of <c>SendMetricsReportAsync</c>. Built per subscribe from the live connection, so it is
/// never a DI singleton (which would create a cycle with the connection, exactly as
/// <see cref="HubOperationProgressReporter"/> avoids for progress).
/// </summary>
internal sealed class ServerLogEmitter : IServerLogEmitter
{
    private readonly HubConnection _connection;
    private readonly TimeProvider _timeProvider;

    public ServerLogEmitter(HubConnection connection, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _connection = connection;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public Task EmitAsync(ServerId serverId, IReadOnlyList<ServerLogLine> lines, bool dropped, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lines);

        // A send while disconnected would throw; the stream is transient and Web re-subscribes on reconnect, so
        // drop the batch rather than fault the flush loop.
        if (_connection.State != HubConnectionState.Connected)
        {
            return Task.CompletedTask;
        }

        Envelope<ServerLogBatch> envelope = Envelope.Create(
            new ServerLogBatch(serverId, lines, dropped), _timeProvider.GetUtcNow(), serverId: serverId);
        return _connection.SendAsync(AgentHubProtocol.ServerLogBatch, envelope, cancellationToken);
    }
}
