using Microsoft.AspNetCore.SignalR;
using ZWarden.Application.Agents;
using ZWarden.Application.Configuration;
using ZWarden.Contracts.Protocol;
using ZWarden.Domain.Ids;

namespace ZWarden.Web.Agents;

/// <summary>
/// The default <see cref="IServerConfigRawEditChannel"/> (F20c PR-D, ADR 0042): chunks an operator-authored
/// whole-file configuration edit and sends it up the owning Agent's SignalR connection over the
/// <see cref="AgentHubProtocol.StageServerConfigRawEdit"/> channel, ahead of the <c>ConfigApplyRaw</c> Operation
/// that applies it — the write-direction sibling of <see cref="ServerConfigReadCoordinator"/>. There is no reply to
/// await: server→client sends on one connection are ordered, so the Agent has the staged text buffered by the time
/// the Operation (dispatched afterward over the same connection) runs; a lost chunk or a reconnect between simply
/// fails that Operation cleanly on the Agent. An Agent that is not connected is reported
/// <see cref="ConfigRawEditStageStatus.AgentOffline"/> at once — nothing is staged and no Operation should be
/// enqueued.
/// </summary>
public sealed partial class ServerConfigRawEditCoordinator : IServerConfigRawEditChannel
{
    private readonly IAgentConnectionRegistry _registry;
    private readonly IHubContext<AgentHub> _hub;
    private readonly ILogger<ServerConfigRawEditCoordinator> _logger;

    public ServerConfigRawEditCoordinator(
        IAgentConnectionRegistry registry,
        IHubContext<AgentHub> hub,
        ILogger<ServerConfigRawEditCoordinator> logger)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(logger);
        _registry = registry;
        _hub = hub;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ConfigRawEditStage> StageAsync(
        ServerId server, AgentId owningAgent, string rawText, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rawText);

        string? connectionId = _registry.GetConnectionId(owningAgent);
        if (connectionId is null)
        {
            LogAgentOffline(owningAgent, server);
            return ConfigRawEditStage.Offline();
        }

        string correlationId = Guid.NewGuid().ToString("N");
        foreach (ServerConfigRawEditChunk chunk in ServerConfigRawEditCodec.Encode(correlationId, rawText))
        {
            await _hub.Clients.Client(connectionId)
                .SendAsync(
                    AgentHubProtocol.StageServerConfigRawEdit,
                    chunk.CorrelationId, chunk.ChunkIndex, chunk.ChunkCount, chunk.Chunk, cancellationToken)
                .ConfigureAwait(false);
        }

        return ConfigRawEditStage.Ok(correlationId);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Agent {AgentId} is offline; cannot stage a raw config edit for server {ServerId}.")]
    private partial void LogAgentOffline(AgentId agentId, ServerId serverId);
}
