using ZWarden.Application.Agents;
using ZWarden.Application.Authorization;
using ZWarden.Application.Settings;
using ZWarden.Application.Setup;
using ZWarden.Application.Workshop;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Setup;

namespace ZWarden.Web.Components.Pages.Settings;

/// <summary>What the Settings page shows that comes from the database: the declared TLS mode, whether a Workshop key is
/// configured, the enrolled and connected host counts (#344, null without <c>Agent.View</c>), the instance name override
/// (#345, null = config applies), and the tenant-wide grants that gate its Owner/Admin-only sections and controls. A
/// plain, JSON-serializable record, so the prerender hands it to the circuit through <c>[PersistentState]</c> (#299).</summary>
public sealed record SettingsState(
    TlsMode? TlsMode,
    bool WorkshopKeyConfigured,
    bool CanManageEnrollment,
    bool CanManageWorkshop,
    bool CanManageRoles,
    int? EnrolledHosts = null,
    int? OnlineHosts = null,
    bool CanManageSettings = false,
    string? InstanceNameOverride = null,
    TimeSpan? SessionIdleTimeout = null);

/// <summary>
/// Loads the Settings page (#299). The interactive page calls it through <c>ActionScopeRunner</c>, a scope per load.
/// It replaces the page's <c>AuthorizeView</c> policies, which would evaluate in the circuit's own long-lived scope
/// (one <c>DbContext</c> shared across concurrent evaluations, the #154 class). Enforcement stays server-side in each
/// service (PRD 12); these flags only decide which sections the sub-nav offers.
/// </summary>
public sealed class SettingsQuery
{
    private readonly ISetupState _setup;
    private readonly IWorkshopSettingsService _workshop;
    private readonly IPermissionChecker _permissions;
    private readonly IAgentInventory _agents;
    private readonly IControlPlaneSettingsService _settings;

    public SettingsQuery(
        ISetupState setup,
        IWorkshopSettingsService workshop,
        IPermissionChecker permissions,
        IAgentInventory agents,
        IControlPlaneSettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(workshop);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(settings);
        _setup = setup;
        _workshop = workshop;
        _permissions = permissions;
        _agents = agents;
        _settings = settings;
    }

    public async Task<SettingsState> LoadAsync(UserId user, CancellationToken ct)
    {
        async Task<bool> Can(PermissionDefinition permission) =>
            (await _permissions.EvaluateAsync(user, permission, cancellationToken: ct).ConfigureAwait(false)).IsAllowed;

        bool canManageEnrollment = await Can(Permissions.TenantEnrollmentManage).ConfigureAwait(false);

        // The Host enrollment section's "N enrolled · M online" (#344): only where that section shows, and only for an
        // operator who may see Hosts at all (the inventory enforces Agent.View itself).
        IReadOnlyList<HostSummary>? hosts = canManageEnrollment && await Can(Permissions.AgentView).ConfigureAwait(false)
            ? await _agents.ListHostsAsync(user, ct).ConfigureAwait(false)
            : null;

        ControlPlaneSettingsSnapshot settings = await _settings.GetAsync(ct).ConfigureAwait(false);
        return new SettingsState(
            TlsMode: await _setup.GetTlsModeAsync(ct).ConfigureAwait(false),
            WorkshopKeyConfigured: await _workshop.IsSearchAvailableAsync(ct).ConfigureAwait(false),
            CanManageEnrollment: canManageEnrollment,
            CanManageWorkshop: await Can(Permissions.TenantManage).ConfigureAwait(false),
            CanManageRoles: await Can(Permissions.RoleManage).ConfigureAwait(false),
            EnrolledHosts: hosts?.Count,
            OnlineHosts: hosts?.Count(h => h.IsConnected),
            CanManageSettings: await Can(Permissions.TenantSettingsManage).ConfigureAwait(false),
            InstanceNameOverride: settings.InstanceName,
            SessionIdleTimeout: settings.SessionIdleTimeout);
    }
}
