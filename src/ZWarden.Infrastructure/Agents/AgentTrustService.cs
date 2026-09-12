using ZWarden.Application.Agents;
using ZWarden.Application.Audit;
using ZWarden.Application.Authorization;
using ZWarden.Domain.Agents;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Security;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Infrastructure.Agents;

/// <summary>
/// The operator-facing Agent trust-management surface (ADR 0007). Every method requires the actor to hold
/// <c>Tenant.Enrollment.Manage</c> tenant-wide (fail-closed) and audits the outcome (F6), with no credential
/// in any audit record. Rotation returns a fresh credential shown once; revoke, rotate and disable each take
/// effect on the next verification. Agents are tenant-owned, read through the tenant filter (ADR 0016).
/// </summary>
public sealed class AgentTrustService : IAgentTrustService
{
    private readonly ZWardenDbContext _context;
    private readonly AgentRepository _agents;
    private readonly IPermissionChecker _checker;
    private readonly IAuditWriter _audit;
    private readonly ICredentialHasher _hasher;
    private readonly TimeProvider _clock;
    private readonly IAgentConnectionRegistry _connections;

    public AgentTrustService(
        ZWardenDbContext context,
        AgentRepository agents,
        IPermissionChecker checker,
        IAuditWriter audit,
        ICredentialHasher hasher,
        TimeProvider clock,
        IAgentConnectionRegistry connections)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(checker);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(connections);
        _context = context;
        _agents = agents;
        _checker = checker;
        _audit = audit;
        _hasher = hasher;
        _clock = clock;
        _connections = connections;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentSummary>> ListAsync(UserId actor, CancellationToken cancellationToken = default)
    {
        await RequireEnrollmentManageAsync(actor, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<Agent> all = await _agents.ListAsync(cancellationToken).ConfigureAwait(false);
        return all
            .Select(a => new AgentSummary(
                a.Id, a.IsEnabled, !string.IsNullOrEmpty(a.CredentialHash), a.EnrolledAt, a.CredentialRotatedAt, a.Label))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<SecretString> RotateCredentialAsync(UserId actor, AgentId agentId, CancellationToken cancellationToken = default)
    {
        await RequireEnrollmentManageAsync(actor, cancellationToken).ConfigureAwait(false);
        Agent agent = await RequireAgentAsync(agentId, cancellationToken).ConfigureAwait(false);

        SecretString credential = _hasher.Generate(CredentialPrefixes.Agent);
        agent.RotateCredential(_hasher.Hash(credential), _clock.GetUtcNow());
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await AuditAsync(EnrollmentAuditActions.CredentialRotated, actor, agent.Id, cancellationToken).ConfigureAwait(false);
        return credential;
    }

    /// <inheritdoc />
    public async Task RevokeCredentialAsync(UserId actor, AgentId agentId, CancellationToken cancellationToken = default)
    {
        await RequireEnrollmentManageAsync(actor, cancellationToken).ConfigureAwait(false);
        Agent agent = await RequireAgentAsync(agentId, cancellationToken).ConfigureAwait(false);

        agent.RevokeCredential(_clock.GetUtcNow());
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await AuditAsync(EnrollmentAuditActions.CredentialRevoked, actor, agent.Id, cancellationToken).ConfigureAwait(false);
        await DropLiveConnectionAsync(actor, agent.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DisableAsync(UserId actor, AgentId agentId, CancellationToken cancellationToken = default)
    {
        await RequireEnrollmentManageAsync(actor, cancellationToken).ConfigureAwait(false);
        Agent agent = await RequireAgentAsync(agentId, cancellationToken).ConfigureAwait(false);

        agent.Disable();
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await AuditAsync(EnrollmentAuditActions.AgentDisabled, actor, agent.Id, cancellationToken).ConfigureAwait(false);
        await DropLiveConnectionAsync(actor, agent.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task EnableAsync(UserId actor, AgentId agentId, CancellationToken cancellationToken = default)
    {
        await RequireEnrollmentManageAsync(actor, cancellationToken).ConfigureAwait(false);
        Agent agent = await RequireAgentAsync(agentId, cancellationToken).ConfigureAwait(false);

        agent.Enable();
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await AuditAsync(EnrollmentAuditActions.AgentEnabled, actor, agent.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// F10: revoke/disable must take effect on an already-connected Agent immediately, not only at its next
    /// reconnect — so drop the live connection here. The registry is per-process (single Web instance, ADR
    /// 0005); a multi-instance deployment (F10A/v1.1) fulfils this through the backplane. A dropped connection
    /// is audited so the operator sees the revoke reached a live Agent.
    /// </summary>
    private Task DropLiveConnectionAsync(UserId actor, AgentId agentId, CancellationToken cancellationToken)
        => _connections.TryAbort(agentId)
            ? AuditAsync(AgentConnectionAuditActions.CredentialRevokedWhileConnected, actor, agentId, cancellationToken)
            : Task.CompletedTask;

    private async Task<Agent> RequireAgentAsync(AgentId agentId, CancellationToken cancellationToken)
        => await _agents.FindByIdAsync(agentId, cancellationToken).ConfigureAwait(false)
            ?? throw new AuthorizationDeniedException("Agent not found in the current tenant.");

    private Task AuditAsync(string action, UserId actor, AgentId agentId, CancellationToken cancellationToken)
        => _audit.WriteAsync(
            new AuditEntry(action, AuditOutcome.Succeeded, actor, null, $"agent {agentId}"),
            cancellationToken);

    private async Task RequireEnrollmentManageAsync(UserId actor, CancellationToken cancellationToken)
    {
        IReadOnlySet<string> held = await _checker.GetTenantWidePermissionsAsync(actor, cancellationToken).ConfigureAwait(false);
        if (!held.Contains(Permissions.TenantEnrollmentManage.Name))
        {
            throw new AuthorizationDeniedException(
                $"{Permissions.TenantEnrollmentManage.Name} is required to manage Agents.");
        }
    }
}
