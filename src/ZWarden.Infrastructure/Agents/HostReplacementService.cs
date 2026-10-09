using Microsoft.EntityFrameworkCore;
using ZWarden.Application.Agents;
using ZWarden.Application.Audit;
using ZWarden.Application.Authorization;
using ZWarden.Application.Settings;
using ZWarden.Application.Tenancy;
using ZWarden.Domain.Agents;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Backups;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;
using ZWarden.Domain.Settings;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Infrastructure.Agents;

/// <summary>
/// The <see cref="IHostReplacementService"/> (#368, ADR 0049): the one path that lets an Agent own containers stamped
/// with another Agent's id. The Owner confirms it. The checks run in order, and a refusal changes nothing:
/// <c>Tenant.Enrollment.Manage</c>, both Hosts in the tenant (read through the tenant filter, ADR 0016), two different
/// Hosts, a trusted successor, an offline predecessor (the live registry is authoritative), and the successor's own
/// latest snapshot reporting containers stamped with the predecessor's id. Then one save moves the predecessor's
/// Servers and backups, passes on the ids it had inherited, records the replacement, re-points the default deploy host,
/// and revokes and deletes the predecessor.
/// </summary>
public sealed class HostReplacementService : IHostReplacementService
{
    private readonly ZWardenDbContext _context;
    private readonly AgentRepository _agents;
    private readonly IPermissionChecker _checker;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _clock;
    private readonly IAgentConnectionRegistry _connections;
    private readonly IForeignContainerCache _foreign;
    private readonly IAgentOwnershipNotifier _notifier;
    private readonly IControlPlaneSettingsCache _settingsCache;
    private readonly ITenantContext _tenant;

    public HostReplacementService(
        ZWardenDbContext context,
        AgentRepository agents,
        IPermissionChecker checker,
        IAuditWriter audit,
        TimeProvider clock,
        IAgentConnectionRegistry connections,
        IForeignContainerCache foreign,
        IAgentOwnershipNotifier notifier,
        IControlPlaneSettingsCache settingsCache,
        ITenantContext tenant)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(checker);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(foreign);
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(settingsCache);
        ArgumentNullException.ThrowIfNull(tenant);
        _context = context;
        _agents = agents;
        _checker = checker;
        _audit = audit;
        _clock = clock;
        _connections = connections;
        _foreign = foreign;
        _notifier = notifier;
        _settingsCache = settingsCache;
        _tenant = tenant;
    }

    /// <inheritdoc />
    public async Task<HostReplacementResult> ReplaceAsync(
        UserId actor, AgentId successor, AgentId predecessor, CancellationToken cancellationToken = default)
    {
        await RequireEnrollmentManageAsync(actor, cancellationToken).ConfigureAwait(false);
        Agent next = await RequireAgentAsync(successor, cancellationToken).ConfigureAwait(false);
        Agent old = await RequireAgentAsync(predecessor, cancellationToken).ConfigureAwait(false);

        if (next.Id == old.Id)
        {
            return HostReplacementResult.Refused(HostReplacementOutcome.SameHost);
        }

        if (!next.IsTrusted)
        {
            return HostReplacementResult.Refused(HostReplacementOutcome.SuccessorUntrusted);
        }

        if (_connections.IsConnected(old.Id))
        {
            return HostReplacementResult.Refused(HostReplacementOutcome.PredecessorOnline);
        }

        // The successor's own Agent must have found containers stamped with the predecessor's id on its machine, so the
        // Owner can only hand a Host's Servers to the machine they are actually on.
        if (!_foreign.GetReported(next.Id).Any(c => c.LabelledAgentId == old.Id))
        {
            return HostReplacementResult.Refused(HostReplacementOutcome.NotReported);
        }

        DateTimeOffset now = _clock.GetUtcNow();
        List<Server> servers = await _context.Set<Server>()
            .Where(s => s.AgentId == old.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        List<Backup> backups = await _context.Set<Backup>()
            .Where(b => b.AgentId == old.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        List<HostReplacement> passedOn = await _context.Set<HostReplacement>()
            .Where(r => r.SuccessorId == old.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        ControlPlaneSettings? settings = await _context.Set<ControlPlaneSettings>()
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        servers.ForEach(s => s.ReassignTo(next.Id));
        backups.ForEach(b => b.ReassignTo(next.Id));
        passedOn.ForEach(r => r.PassTo(next.Id));
        _context.Add(HostReplacement.Record(next.Id, old.Id, actor, now));
        bool movedDefault = settings?.DefaultDeployHost == old.Id;
        if (movedDefault)
        {
            settings!.SetDefaultDeployHost(next.Id);
        }

        // Delete before the abort, so a reconnect racing it already finds no credential to match (as Remove host).
        old.RevokeCredential(now);
        _context.Remove(old);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (movedDefault)
        {
            _settingsCache.Remember(
                _tenant.CurrentTenantId,
                new ControlPlaneSettingsSnapshot(settings!.InstanceName, settings.SessionIdleTimeout, settings.DefaultDeployHost));
        }

        await _audit.WriteAsync(
            new AuditEntry(
                EnrollmentAuditActions.AgentReplaced,
                AuditOutcome.Succeeded,
                actor,
                null,
                $"agent {next.Id}; replaced {NameOf(old)} (agent {old.Id}); {servers.Count} server(s), {backups.Count} backup(s) moved"),
            cancellationToken).ConfigureAwait(false);

        _connections.TryAbort(old.Id);
        _foreign.Forget(next.Id);
        await _notifier.OwnershipChangedAsync(next.Id, cancellationToken).ConfigureAwait(false);
        return new HostReplacementResult(HostReplacementOutcome.Replaced, servers.Count, backups.Count);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentId>> InheritedIdsAsync(AgentId successor, CancellationToken cancellationToken = default)
        => await _context.Set<HostReplacement>()
            .Where(r => r.SuccessorId == successor)
            .Select(r => r.PredecessorId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    private static string NameOf(Agent agent) => agent.Label ?? agent.Hostname ?? agent.Id.ToString();

    private async Task<Agent> RequireAgentAsync(AgentId agentId, CancellationToken cancellationToken)
        => await _agents.FindByIdAsync(agentId, cancellationToken).ConfigureAwait(false)
            ?? throw new AuthorizationDeniedException("Agent not found in the current tenant.");

    private async Task RequireEnrollmentManageAsync(UserId actor, CancellationToken cancellationToken)
    {
        IReadOnlySet<string> held = await _checker.GetTenantWidePermissionsAsync(actor, cancellationToken).ConfigureAwait(false);
        if (!held.Contains(Permissions.TenantEnrollmentManage.Name))
        {
            throw new AuthorizationDeniedException(
                $"{Permissions.TenantEnrollmentManage.Name} is required to replace hosts.");
        }
    }
}
