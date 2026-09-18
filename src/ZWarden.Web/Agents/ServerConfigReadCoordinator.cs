using Microsoft.AspNetCore.SignalR;
using ZWarden.Application.Agents;
using ZWarden.Application.Configuration;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;

namespace ZWarden.Web.Agents;

/// <summary>
/// The default <see cref="IServerConfigReadChannel"/> (F20c, ADR 0041): the correlating coordinator for a live
/// configuration read, the request/reply sibling of the F27 log-subscription coordinator. A singleton — a pending
/// read is shared with the <see cref="AgentHub"/> that ingests the reply. On a read it sends
/// <see cref="AgentHubProtocol.RequestServerConfigRead"/> to the owning Agent's connection with a fresh correlation
/// id, then awaits the reply chunks the hub feeds back through <see cref="AcceptChunk"/>, reassembling them with the
/// shared codec. An Agent that is not connected is reported <see cref="ConfigTransferStatus.AgentOffline"/> at once;
/// one that is connected but silent times out. A reply is accepted only from the Agent the read was routed to and
/// only against a live correlation id (the ownership guard, trust-boundaries.md §8). Nothing is persisted.
/// </summary>
public sealed partial class ServerConfigReadCoordinator : IServerConfigReadChannel
{
    /// <summary>The default wait for a connected Agent's reply before giving up. Generous: a read is one file.</summary>
    public static readonly TimeSpan DefaultReadTimeout = TimeSpan.FromSeconds(30);

    private readonly IAgentConnectionRegistry _registry;
    private readonly IHubContext<AgentHub> _hub;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ServerConfigReadCoordinator> _logger;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, PendingRead> _pending = new(StringComparer.Ordinal);

    public ServerConfigReadCoordinator(
        IAgentConnectionRegistry registry,
        IHubContext<AgentHub> hub,
        TimeProvider timeProvider,
        ILogger<ServerConfigReadCoordinator> logger)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _registry = registry;
        _hub = hub;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>How long a connected Agent has to reply before the read times out (defaults to
    /// <see cref="DefaultReadTimeout"/>).</summary>
    public TimeSpan ReadTimeout { get; init; } = DefaultReadTimeout;

    /// <inheritdoc />
    public async Task<ConfigReadTransfer> ReadAsync(
        ServerId server, AgentId owningAgent, PzConfigFile file, CancellationToken cancellationToken = default)
    {
        string? connectionId = _registry.GetConnectionId(owningAgent);
        if (connectionId is null)
        {
            LogAgentOffline(owningAgent, server);
            return ConfigReadTransfer.OfStatus(ConfigTransferStatus.AgentOffline);
        }

        string correlationId = Guid.NewGuid().ToString("N");
        PendingRead pending = new(owningAgent);
        _pending[correlationId] = pending;
        try
        {
            await _hub.Clients.Client(connectionId)
                .SendAsync(AgentHubProtocol.RequestServerConfigRead, server.ToString(), file.ToString(), correlationId, cancellationToken)
                .ConfigureAwait(false);

            Task delay = Task.Delay(ReadTimeout, _timeProvider, cancellationToken);
            Task finished = await Task.WhenAny(pending.Completion.Task, delay).ConfigureAwait(false);
            if (finished == pending.Completion.Task)
            {
                return await pending.Completion.Task.ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            LogReadTimedOut(owningAgent, server);
            return ConfigReadTransfer.OfStatus(ConfigTransferStatus.TimedOut);
        }
        finally
        {
            _pending.TryRemove(correlationId, out _);
        }
    }

    /// <summary>
    /// Ingests one reply chunk from the Agent hub (F20c). Dropped silently when it matches no live read (unknown or
    /// expired correlation id) or when its reporting Agent is not the one the read was routed to (the ownership
    /// guard). When the chunk completes a set, the reassembled view is decoded and the pending read completed; a set
    /// that is oversized or does not decode completes the read as a failure rather than hanging until the timeout.
    /// </summary>
    public void AcceptChunk(AgentId reportingAgent, Envelope<ServerConfigContent> content)
    {
        ArgumentNullException.ThrowIfNull(content);
        ServerConfigContent chunk = content.Payload;

        if (!_pending.TryGetValue(chunk.CorrelationId, out PendingRead? pending))
        {
            return; // no live read for this correlation id — unknown, or already completed/expired
        }

        if (reportingAgent != pending.ExpectedAgent)
        {
            LogForeignReply(reportingAgent, pending.ExpectedAgent);
            return; // one Agent cannot answer another's read (trust-boundaries.md §8)
        }

        if (chunk.ChunkCount < 1 || chunk.ChunkCount > ServerConfigContentCodec.MaxChunkCount)
        {
            pending.Completion.TrySetResult(ConfigReadTransfer.OfStatus(ConfigTransferStatus.TimedOut));
            return;
        }

        if (!pending.TryCollect(chunk, out IReadOnlyList<ServerConfigContent>? complete))
        {
            return; // still waiting for more chunks (or a duplicate/out-of-range index was ignored)
        }

        try
        {
            pending.Completion.TrySetResult(ToTransfer(ServerConfigContentCodec.Decode(complete)));
        }
#pragma warning disable CA1031 // A corrupt reply must not throw out of a hub callback; surface it as a failed read.
        catch (Exception ex)
        {
            LogUndecodableReply(ex);
            pending.Completion.TrySetResult(ConfigReadTransfer.OfStatus(ConfigTransferStatus.TimedOut));
        }
#pragma warning restore CA1031
    }

    private static ConfigReadTransfer ToTransfer(ConfigReadPayload payload) =>
        new(
            payload.Status switch
            {
                ConfigReadStatus.Read => ConfigTransferStatus.Read,
                ConfigReadStatus.ParseFailed => ConfigTransferStatus.ParseFailed,
                _ => ConfigTransferStatus.FileMissing,
            },
            [.. payload.Settings.Select(s => new ConfigTransferSetting(s.Path, ToEditKind(s.Kind), s.Value, s.Comment))],
            payload.RawText,
            payload.BaselineHash,
            [.. payload.Diagnostics.Select(d => new ConfigTransferDiagnostic(d.Message, d.Line, d.Column))]);

    private static ConfigEditKind ToEditKind(ConfigValueKind kind) => kind switch
    {
        ConfigValueKind.Bool => ConfigEditKind.Bool,
        ConfigValueKind.Number => ConfigEditKind.Number,
        _ => ConfigEditKind.Text,
    };

    // One outstanding read: the Agent it was routed to, the chunks collected so far (deduped by index), and the
    // task the ReadAsync caller awaits.
    private sealed class PendingRead(AgentId expectedAgent)
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<int, ServerConfigContent> _chunks = [];

        public AgentId ExpectedAgent { get; } = expectedAgent;

        public TaskCompletionSource<ConfigReadTransfer> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        // Adds a chunk (ignoring an out-of-range or duplicate index) and, once every index is present, hands back the
        // full set exactly once.
        public bool TryCollect(ServerConfigContent chunk, out IReadOnlyList<ServerConfigContent> complete)
        {
            complete = [];
            lock (_gate)
            {
                if (chunk.ChunkIndex < 0 || chunk.ChunkIndex >= chunk.ChunkCount || !_chunks.TryAdd(chunk.ChunkIndex, chunk))
                {
                    return false;
                }

                if (_chunks.Count != chunk.ChunkCount)
                {
                    return false;
                }

                complete = [.. _chunks.Values];
                return true;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Agent {AgentId} is offline; cannot read configuration for server {ServerId}.")]
    private partial void LogAgentOffline(AgentId agentId, ServerId serverId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Agent {AgentId} did not reply to the configuration read for server {ServerId} in time.")]
    private partial void LogReadTimedOut(AgentId agentId, ServerId serverId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignoring a configuration read reply from agent {ReportingAgent} for a read routed to {ExpectedAgent}.")]
    private partial void LogForeignReply(AgentId reportingAgent, AgentId expectedAgent);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A configuration read reply could not be reassembled; reporting it as a failed read.")]
    private partial void LogUndecodableReply(Exception ex);
}
