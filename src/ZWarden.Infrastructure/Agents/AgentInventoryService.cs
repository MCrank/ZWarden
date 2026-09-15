using ZWarden.Application.Agents;
using ZWarden.Application.Authorization;
using ZWarden.Domain.Agents;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;

namespace ZWarden.Infrastructure.Agents;

/// <summary>
/// The read-only Host inventory (F35) backing the operator <c>/hosts</c> page. It requires the actor to hold
/// <c>Agent.View</c> tenant-wide (fail-closed, D-2), lists the tenant's trusted Agents through the tenant
/// filter (ADR 0016), and projects each into a <see cref="HostSummary"/> — overlaying the in-memory
/// <see cref="IAgentConnectionRegistry"/>'s authoritative "connected right now" over the persisted last-known
/// <see cref="AgentConnectionState"/>, which is all a Web restart leaves behind.
/// </summary>
public sealed class AgentInventoryService : IAgentInventory
{
    private readonly AgentRepository _agents;
    private readonly IPermissionChecker _checker;
    private readonly IAgentConnectionRegistry _connections;

    public AgentInventoryService(
        AgentRepository agents,
        IPermissionChecker checker,
        IAgentConnectionRegistry connections)
    {
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(checker);
        ArgumentNullException.ThrowIfNull(connections);
        _agents = agents;
        _checker = checker;
        _connections = connections;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<HostSummary>> ListHostsAsync(UserId actor, CancellationToken cancellationToken = default)
    {
        IReadOnlySet<string> held = await _checker.GetTenantWidePermissionsAsync(actor, cancellationToken).ConfigureAwait(false);
        if (!held.Contains(Permissions.AgentView.Name))
        {
            throw new AuthorizationDeniedException($"{Permissions.AgentView.Name} is required to view Hosts.");
        }

        IReadOnlyList<Agent> all = await _agents.ListAsync(cancellationToken).ConfigureAwait(false);
        return all
            .Select(a => new HostSummary(
                a.Id,
                a.Label,
                a.Hostname,
                a.AgentVersion,
                a.OsPlatform,
                a.IsEnabled,
                !string.IsNullOrEmpty(a.CredentialHash),
                a.ConnectionState,
                _connections.IsConnected(a.Id),
                a.LastSeenAt,
                a.LastProtocolVersion,
                a.EnrolledAt))
            .ToList();
    }
}
