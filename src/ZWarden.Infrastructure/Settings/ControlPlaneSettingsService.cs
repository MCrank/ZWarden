using ZWarden.Application.Audit;
using ZWarden.Application.Authorization;
using ZWarden.Application.Settings;
using ZWarden.Application.Tenancy;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Settings;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Infrastructure.Settings;

/// <summary>
/// The operator-edited control-plane settings (#345, ADR 0048). Changes require <c>Tenant.Settings.Manage</c>
/// tenant-wide (fail-closed, PRD 12), are validated by the aggregate, audited old → new, and pushed into the in-process
/// cache so the shell shows them at once. The row is tenant-owned: the interceptor stamps the ambient tenant and the
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

    public ControlPlaneSettingsService(
        ZWardenDbContext context,
        ControlPlaneSettingsRepository settings,
        IPermissionChecker checker,
        IAuditWriter audit,
        IControlPlaneSettingsCache cache,
        ITenantContext tenant)
    {
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
    public async Task SetInstanceNameAsync(UserId actor, string? name, CancellationToken cancellationToken = default)
    {
        await RequireManageAsync(actor, cancellationToken).ConfigureAwait(false);

        ControlPlaneSettings? existing = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);
        ControlPlaneSettings row = existing ?? ControlPlaneSettings.Create();
        string? before = row.InstanceName;
        row.SetInstanceName(name); // validates; throws before anything is stored
        if (string.Equals(before, row.InstanceName, StringComparison.Ordinal))
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
            new AuditEntry(
                SettingsAuditActions.InstanceNameChanged,
                AuditOutcome.Succeeded,
                actor,
                null,
                $"instance name: {Describe(before)} → {Describe(row.InstanceName)}"),
            cancellationToken).ConfigureAwait(false);
    }

    private static ControlPlaneSettingsSnapshot Snapshot(ControlPlaneSettings? row) =>
        row is null ? ControlPlaneSettingsSnapshot.Empty : new ControlPlaneSettingsSnapshot(row.InstanceName);

    private static string Describe(string? value) => value is null ? "(config)" : $"\"{value}\"";

    private async Task RequireManageAsync(UserId actor, CancellationToken cancellationToken)
    {
        IReadOnlySet<string> held = await _checker.GetTenantWidePermissionsAsync(actor, cancellationToken).ConfigureAwait(false);
        if (!held.Contains(Permissions.TenantSettingsManage.Name))
        {
            throw new AuthorizationDeniedException(
                $"{Permissions.TenantSettingsManage.Name} is required to change control-plane settings.");
        }
    }
}
