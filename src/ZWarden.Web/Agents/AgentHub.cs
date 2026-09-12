using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using ZWarden.Application.Agents;
using ZWarden.Application.Audit;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Agents;

namespace ZWarden.Web.Agents;

/// <summary>
/// The SignalR hub an Agent connects to outbound over WSS (F10; criterion 14 — no inbound host port). The
/// <see cref="AgentAuthenticationHandler.SchemeName"/> scheme has already authenticated the connection by the
/// time any method here runs, so the Agent id is trusted off the connection principal. On connect the Agent
/// is registered in the in-memory <see cref="IAgentConnectionRegistry"/> (the seam F11 dispatches through);
/// <see cref="Hello"/> negotiates the protocol version (ADR 0020) and aborts an incompatible peer; heartbeats
/// and the post-connect snapshot advance the persisted last-seen. Connection lifecycle is audited (F6) with
/// no credential in any record.
/// </summary>
[Authorize(AuthenticationSchemes = AgentAuthenticationHandler.SchemeName)]
public sealed partial class AgentHub : Hub
{
    private readonly IAgentConnectionRegistry _registry;
    private readonly IAgentConnectionStateWriter _state;
    private readonly IAuditWriter _audit;
    private readonly ILogger<AgentHub> _logger;

    public AgentHub(
        IAgentConnectionRegistry registry,
        IAgentConnectionStateWriter state,
        IAuditWriter audit,
        ILogger<AgentHub> logger)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(logger);
        _registry = registry;
        _state = state;
        _audit = audit;
        _logger = logger;
    }

    /// <inheritdoc />
    public override async Task OnConnectedAsync()
    {
        if (!AgentClaims.TryGetAgentId(Context.User, out AgentId agentId))
        {
            // The scheme guarantees the claim; this is defensive — refuse a principal we cannot address.
            Context.Abort();
            return;
        }

        _registry.Register(agentId, Context.ConnectionId, Context.Abort);
        await _audit.WriteAsync(Entry(AgentConnectionAuditActions.Connected, agentId), CancellationToken.None)
            .ConfigureAwait(false);
        LogConnected(agentId);
        await base.OnConnectedAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _registry.Remove(Context.ConnectionId);
        if (AgentClaims.TryGetAgentId(Context.User, out AgentId agentId))
        {
            // The connection token is already cancelled here, so persist under None or the write is cancelled.
            await _state.MarkDisconnectedAsync(agentId, CancellationToken.None).ConfigureAwait(false);
            await _audit.WriteAsync(Entry(AgentConnectionAuditActions.Disconnected, agentId), CancellationToken.None)
                .ConfigureAwait(false);
            LogDisconnected(agentId);
        }

        await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
    }

    /// <summary>
    /// The Agent's opening call: negotiate the protocol version carried on its <see cref="AgentHello"/>
    /// envelope (ADR 0020). An incompatible peer is told the actionable reason and its connection is aborted;
    /// a compatible one has its negotiated version persisted.
    /// </summary>
    public async Task<ProtocolNegotiationResult> Hello(Envelope<AgentHello> hello)
    {
        ArgumentNullException.ThrowIfNull(hello);
        AgentClaims.TryGetAgentId(Context.User, out AgentId agentId);

        ProtocolNegotiationResult result =
            ProtocolCompatibility.Negotiate(hello.ProtocolVersion, ProtocolVersionRange.Supported);

        if (!result.IsCompatible)
        {
            await _audit.WriteAsync(
                new AuditEntry(
                    AgentConnectionAuditActions.ConnectionRejected,
                    AuditOutcome.Failed,
                    null,
                    null,
                    $"agent {agentId}; {result.RejectionReason}"),
                CancellationToken.None).ConfigureAwait(false);
            LogProtocolRejected(agentId, result.RejectionReason ?? "incompatible");
            // Abort after this call returns, so the Agent reliably receives the negotiation result (and its
            // actionable reason) before the connection is torn down — aborting synchronously would cancel the
            // in-flight invocation.
            ScheduleAbort();
            return result;
        }

        await _state.MarkConnectedAsync(agentId, hello.ProtocolVersion, Context.ConnectionAborted).ConfigureAwait(false);
        return result;
    }

    /// <summary>Periodic liveness from the Agent (PRD 40): advance the persisted last-seen.</summary>
    public async Task Heartbeat(Envelope<AgentHeartbeat> heartbeat)
    {
        ArgumentNullException.ThrowIfNull(heartbeat);
        if (AgentClaims.TryGetAgentId(Context.User, out AgentId agentId))
        {
            await _state.MarkHeartbeatAsync(agentId, Context.ConnectionAborted).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The Agent's post-(re)connect state report. F10 wires the channel end to end; the payload is host-level
    /// (empty server set) until F13/F14 give the Agent a Docker runtime and Server inventory. Advances the
    /// persisted last-seen.
    /// </summary>
    public async Task StateSnapshot(Envelope<AgentStateSnapshot> snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (AgentClaims.TryGetAgentId(Context.User, out AgentId agentId))
        {
            await _state.MarkHeartbeatAsync(agentId, Context.ConnectionAborted).ConfigureAwait(false);
            LogSnapshot(agentId, snapshot.Payload.Servers.Count);
        }
    }

    private static AuditEntry Entry(string action, AgentId agentId)
        => new(action, AuditOutcome.Succeeded, null, null, $"agent {agentId}");

    // Abort shortly after the current invocation returns, giving its result time to reach the Agent.
    private void ScheduleAbort()
    {
        HubCallerContext context = Context;
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
            context.Abort();
        });
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent {AgentId} connected to the control plane.")]
    private partial void LogConnected(AgentId agentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent {AgentId} disconnected from the control plane.")]
    private partial void LogDisconnected(AgentId agentId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Agent {AgentId} rejected at negotiation: {Reason}")]
    private partial void LogProtocolRejected(AgentId agentId, string reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Agent {AgentId} reported a state snapshot with {ServerCount} server(s).")]
    private partial void LogSnapshot(AgentId agentId, int serverCount);
}
