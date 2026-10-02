using ZWarden.Application.Authorization;
using ZWarden.Application.Setup;
using ZWarden.Application.Workshop;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Setup;

namespace ZWarden.Web.Components.Pages.Settings;

/// <summary>What the Settings page shows that comes from the database: the declared TLS mode, whether a Workshop key is
/// configured, and the tenant-wide grants that gate its Owner/Admin-only cards. A plain, JSON-serializable record, so
/// the prerender hands it to the circuit through <c>[PersistentState]</c> (#299).</summary>
public sealed record SettingsState(
    TlsMode? TlsMode, bool WorkshopKeyConfigured, bool CanManageEnrollment, bool CanManageWorkshop, bool CanManageRoles);

/// <summary>
/// Loads the Settings page (#299). The interactive page calls it through <c>ActionScopeRunner</c>, a scope per load.
/// It replaces the page's <c>AuthorizeView</c> policies, which would evaluate in the circuit's own long-lived scope
/// (one <c>DbContext</c> shared across concurrent evaluations, the #154 class). Enforcement stays server-side in each
/// service (PRD 12); these flags only decide which cards render.
/// </summary>
public sealed class SettingsQuery
{
    private readonly ISetupState _setup;
    private readonly IWorkshopSettingsService _workshop;
    private readonly IPermissionChecker _permissions;

    public SettingsQuery(ISetupState setup, IWorkshopSettingsService workshop, IPermissionChecker permissions)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(workshop);
        ArgumentNullException.ThrowIfNull(permissions);
        _setup = setup;
        _workshop = workshop;
        _permissions = permissions;
    }

    public async Task<SettingsState> LoadAsync(UserId user, CancellationToken ct)
    {
        async Task<bool> Can(PermissionDefinition permission) =>
            (await _permissions.EvaluateAsync(user, permission, cancellationToken: ct).ConfigureAwait(false)).IsAllowed;

        return new SettingsState(
            TlsMode: await _setup.GetTlsModeAsync(ct).ConfigureAwait(false),
            WorkshopKeyConfigured: await _workshop.IsSearchAvailableAsync(ct).ConfigureAwait(false),
            CanManageEnrollment: await Can(Permissions.TenantEnrollmentManage).ConfigureAwait(false),
            CanManageWorkshop: await Can(Permissions.TenantManage).ConfigureAwait(false),
            CanManageRoles: await Can(Permissions.RoleManage).ConfigureAwait(false));
    }
}
