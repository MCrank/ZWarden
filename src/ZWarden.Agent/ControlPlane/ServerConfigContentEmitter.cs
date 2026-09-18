using Microsoft.AspNetCore.SignalR.Client;
using ZWarden.Agent.ServerConfig;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;

namespace ZWarden.Agent.ControlPlane;

/// <summary>
/// The <see cref="IServerConfigContentEmitter"/> over a live <see cref="HubConnection"/> (F20c, ADR 0041). It splits
/// the read payload with the shared <see cref="ServerConfigContentCodec"/> and sends each chunk as an
/// <c>Envelope&lt;ServerConfigContent&gt;</c> on <see cref="AgentHubProtocol.ServerConfigContent"/>, stamped with the
/// Server the read is for. Built per-connection (like <c>ServerLogEmitter</c> / <c>HubOperationProgressReporter</c>),
/// so it never becomes a DI singleton coupled to the connection.
/// </summary>
internal sealed class ServerConfigContentEmitter : IServerConfigContentEmitter
{
    private readonly HubConnection _connection;
    private readonly TimeProvider _timeProvider;

    public ServerConfigContentEmitter(HubConnection connection, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _connection = connection;
        _timeProvider = timeProvider;
    }

    public async Task EmitAsync(string correlationId, ConfigReadPayload payload, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(correlationId);
        ArgumentNullException.ThrowIfNull(payload);

        if (_connection.State != HubConnectionState.Connected)
        {
            return;
        }

        foreach (ServerConfigContent chunk in ServerConfigContentCodec.Encode(correlationId, payload))
        {
            Envelope<ServerConfigContent> envelope =
                Envelope.Create(chunk, _timeProvider.GetUtcNow(), serverId: payload.ServerId);
            await _connection.SendAsync(AgentHubProtocol.ServerConfigContent, envelope, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
