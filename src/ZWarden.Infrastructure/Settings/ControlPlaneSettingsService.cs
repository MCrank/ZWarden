using ZWarden.Application.Audit;
using ZWarden.Application.Authorization;
using ZWarden.Application.Settings;
using ZWarden.Application.Tenancy;
using ZWarden.Domain.Agents;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Settings;
using ZWarden.Infrastructure.Agents;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Infrastructure.Settings;

/// <summary>
/// The operator-edited control-plane settings (#345, ADR 0048). Each change is authorized tenant-wide (fail-closed,
/// PRD 12): the instance name on <c>Tenant.Settings.Manage</c>, the session idle timeout on <c>Tenant.Manage</c>
/// (#346), the default deploy host on <c>Tenant.Settings.Manage</c> (#347). The aggregate validates; a change is audited old → new and pushed into the in-process cache, so the shell and
/// the session cookie see it at once. The row is tenant-owned: the interceptor stamps the ambient tenant and the
/// repository reads through the tenant filter (ADR 0016).
/// </summary>
public sealed class ControlPlaneSettingsService : IControlPlaneSettingsService
{
    private readonly ZWardenDbContext _context;
    private readonly ControlPlaneSettingsRepository _settings;
    private readonly IPermissionChecker _checker;
    private readonly IAuditWriter _audit;
    private readonly IControlPlaneSettingsCache _cache;
    private readonly ITenantContext _tenant;
    private readonly AgentRepository _agents;

    public ControlPlaneSettingsService(
        ZWardenDbContext context,
        ControlPlaneSettingsRepository settings,
        IPermissionChecker checker,
        IAuditWriter audit,
        IControlPlaneSettingsCache cache,
        ITenantContext tenant,
        AgentRepository agents)
    {
        ArgumentNullException.ThrowIfNull(agents);
        _agents = agents;
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(checker);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(tenant);
        _context = context;
        _settings = settings;
        _checker = checker;
        _audit = audit;
        _cache = cache;
        _tenant = tenant;
    }

    /// <inheritdoc />
    public async Task<ControlPlaneSettingsSnapshot> GetAsync(CancellationToken cancellationToken = default) =>
        Snapshot(await _settings.GetAsync(cancellationToken).ConfigureAwait(false));

    /// <inheritdoc />
    public Task SetInstanceNameAsync(UserId actor, string? name, CancellationToken cancellationToken = default) =>
        ChangeAsync(
            actor,
            Permissions.TenantSettingsManage,
            SettingsAuditActions.InstanceNameChanged,
            "instance name",
            row => row.InstanceName is { } value ? $"\"{value}\"" : "(config)",
            row => row.SetInstanceName(name),
            cancellationToken);

    /// <inheritdoc />
    public Task SetSessionIdleTimeoutAsync(UserId actor, TimeSpan? timeout, CancellationToken cancellationToken = default) =>
        ChangeAsync(
            actor,
            Permissions.TenantManage,
            SettingsAuditActions.SessionTimeoutChanged,
            "session timeout",
            row => row.SessionIdleTimeout is { } value
                ? SessionTimeouts.Describe(value)
                : $"{SessionTimeouts.Describe(ControlPlaneSettings.DefaultSessionIdleTimeout)} (default)",
            row => row.SetSessionIdleTimeout(timeout),
            cancellationToken);

    /// <inheritdoc />
    public async Task SetDefaultDeployHostAsync(UserId actor, AgentId? host, CancellationToken cancellationToken = default)
    {
        // The tenant's hosts: the trust check for the new host, and names for the audit (the old one may since have been
        // revoked or removed, so it may be only an id).
        Dictionary<AgentId, Agent> agents = (await _agents.ListAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(a => a.Id);

        await ChangeAsync(
            actor,
            Permissions.TenantSettingsManage,
            SettingsAuditActions.DefaultDeployHostChanged,
            "default deploy host",
            row => Describe(row.DefaultDeployHost),
            row =>
            {
                if (host is { } value && !(agents.TryGetValue(value, out Agent? agent) && agent.IsTrusted))
                {
                    throw new ArgumentException("The default deploy host must be an enrolled, enabled host.", nameof(host));
                }

                row.SetDefaultDeployHost(host);
            },
            cancellationToken).ConfigureAwait(false);

        string Describe(AgentId? id) => id switch
        {
            null => "(none)",
            { } value when agents.TryGetValue(value, out Agent? agent) => $"\"{agent.Label ?? agent.Hostname ?? value.ToString()}\"",
            { } value => value.ToString(),
        };
    }

    // Authorizes, applies (the aggregate validates and throws before anything is stored), and on a real change saves,
    // refreshes the cache and audits "<setting>: <before> → <after>". A change to the current value does nothing.
    private async Task ChangeAsync(
        UserId actor,
        PermissionDefinition required,
        string auditAction,
        string setting,
        Func<ControlPlaneSettings, string> describe,
        Action<ControlPlaneSettings> apply,
        CancellationToken cancellationToken)
    {
        IReadOnlySet<string> held = await _checker.GetTenantWidePermissionsAsync(actor, cancellationToken).ConfigureAwait(false);
        if (!held.Contains(required.Name))
        {
            throw new AuthorizationDeniedException($"{required.Name} is required to change the {setting}.");
        }

        ControlPlaneSettings? existing = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);
        ControlPlaneSettings row = existing ?? ControlPlaneSettings.Create();
        string before = describe(row);
        ControlPlaneSettingsSnapshot unchanged = Snapshot(row);
        apply(row);
        if (Snapshot(row) == unchanged)
        {
            return;
        }

        if (existing is null)
        {
            _settings.Add(row);
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _cache.Remember(_tenant.CurrentTenantId, Snapshot(row));

        await _audit.WriteAsync(
            new AuditEntry(auditAction, AuditOutcome.Succeeded, actor, null, $"{setting}: {before} → {describe(row)}"),
            cancellationToken).ConfigureAwait(false);
    }

    private static ControlPlaneSettingsSnapshot Snapshot(ControlPlaneSettings? row) =>
        row is null ? ControlPlaneSettingsSnapshot.Empty : new ControlPlaneSettingsSnapshot(row.InstanceName, row.SessionIdleTimeout, row.DefaultDeployHost);
}
