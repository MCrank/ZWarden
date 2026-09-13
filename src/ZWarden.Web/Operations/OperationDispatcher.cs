using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using ZWarden.Application.Agents;
using ZWarden.Application.Audit;
using ZWarden.Application.Operations;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Operations;
using ZWarden.Infrastructure.Operations;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Web.Agents;

namespace ZWarden.Web.Operations;

/// <summary>
/// The real <see cref="IOperationDispatcher"/> (F11): it sends an operation's command down the F10 SignalR
/// connection. It resolves the Agent's live connection from the in-memory <see cref="IAgentConnectionRegistry"/>
/// (the seam F10 built for exactly this), and — to avoid the "completed before Running was persisted" race —
/// transitions the operation to <see cref="OperationState.Running"/> under a lease and <b>persists that
/// before the command leaves</b>, then sends. With no live connection it leaves the operation
/// <see cref="OperationState.Pending"/> and returns <c>false</c>. The command travels as the canonical wire
/// string of an <c>Envelope&lt;AgentCommand&gt;</c> (<see cref="AgentHubProtocol.ReceiveCommand"/>), so one
/// channel carries the whole closed vocabulary.
/// </summary>
public sealed class OperationDispatcher : IOperationDispatcher
{
    private readonly IAgentConnectionRegistry _registry;
    private readonly IHubContext<AgentHub> _hub;
    private readonly ZWardenDbContext _context;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _clock;
    private readonly OperationEngineOptions _options;

    public OperationDispatcher(
        IAgentConnectionRegistry registry,
        IHubContext<AgentHub> hub,
        ZWardenDbContext context,
        IAuditWriter audit,
        TimeProvider clock,
        IOptions<OperationEngineOptions> options)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);
        _registry = registry;
        _hub = hub;
        _context = context;
        _audit = audit;
        _clock = clock;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<bool> TryDispatchAsync(Operation operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        string? connectionId = _registry.GetConnectionId(operation.AgentId);
        if (connectionId is null)
        {
            // The Agent is offline; leave the operation Pending for a later dispatch (F11 has no reconnect
            // pump yet — the enqueue is the trigger).
            return false;
        }

        DateTimeOffset now = _clock.GetUtcNow();
        operation.MarkDispatched(now + _options.LeaseDuration, now);
        // Persist Running before the command leaves, so an Agent that replies instantly never races a
        // not-yet-persisted transition.
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _audit.WriteAsync(
            new AuditEntry(OperationAuditActions.Started, AuditOutcome.Succeeded, ServerId: operation.ServerId,
                Detail: $"{operation.Kind} {operation.Id}"),
            cancellationToken).ConfigureAwait(false);

        AgentCommand command = operation.Kind switch
        {
            OperationKind.DiagnosticsPing => new PingAgent(),
            _ => throw new NotSupportedException($"No command mapping for operation kind '{operation.Kind}'."),
        };
        Envelope<AgentCommand> envelope = Envelope.Create<AgentCommand>(
            command, now, agentId: operation.AgentId, serverId: operation.ServerId, operationId: operation.Id);

        await _hub.Clients.Client(connectionId)
            .SendAsync(AgentHubProtocol.ReceiveCommand, ProtocolJson.Serialize(envelope), cancellationToken)
            .ConfigureAwait(false);
        return true;
    }
}
