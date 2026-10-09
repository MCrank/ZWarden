using ZWarden.Domain.Ids;

namespace ZWarden.Application.Settings;

/// <summary>
/// The operator-edited control-plane settings (#345, ADR 0048): one row per tenant, each value overriding its config
/// key. Reads are open to any caller in the tenant (the shell needs the instance name on every page); every change is
/// authorized in here (PRD 12) and audited old → new.
/// </summary>
public interface IControlPlaneSettingsService
{
    /// <summary>The ambient tenant's overrides; every value null when nothing has been set.</summary>
    Task<ControlPlaneSettingsSnapshot> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the instance name, or clears it (null or blank) so <c>ZWarden:Instance:Name</c> applies again. Requires
    /// <c>Tenant.Settings.Manage</c>. A value equal to the current one changes and audits nothing.
    /// </summary>
    /// <exception cref="ZWarden.Application.Authorization.AuthorizationDeniedException">The actor may not manage settings.</exception>
    /// <exception cref="ArgumentException">The name is too long or holds a control character.</exception>
    Task SetInstanceNameAsync(UserId actor, string? name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the operator session idle timeout (#346) to one of <c>ControlPlaneSettings.SessionIdleTimeoutChoices</c>, or
    /// back to the default (null). Requires <c>Tenant.Manage</c>. Sessions pick it up on their next request.
    /// </summary>
    /// <exception cref="ZWarden.Application.Authorization.AuthorizationDeniedException">The actor may not change it.</exception>
    /// <exception cref="ArgumentException">The timeout is not one of the choices.</exception>
    Task SetSessionIdleTimeoutAsync(UserId actor, TimeSpan? timeout, CancellationToken cancellationToken = default);
}

/// <summary>A tenant's control-plane setting overrides at one moment; null = not overridden (config applies).</summary>
public sealed record ControlPlaneSettingsSnapshot(string? InstanceName, TimeSpan? SessionIdleTimeout = null)
{
    /// <summary>No overrides.</summary>
    public static ControlPlaneSettingsSnapshot Empty { get; } = new((string?)null);
}

/// <summary>
/// The in-process cache of each tenant's <see cref="ControlPlaneSettingsSnapshot"/> (ADR 0048), for readers on hot
/// paths (the shell's instance name on every page). A miss loads in a fresh tenant scope; the settings service
/// refreshes an entry after each save.
/// </summary>
public interface IControlPlaneSettingsCache
{
    /// <summary>The tenant's current overrides.</summary>
    Task<ControlPlaneSettingsSnapshot> GetAsync(TenantId tenant, CancellationToken cancellationToken = default);

    /// <summary>Records the tenant's overrides after a save.</summary>
    void Remember(TenantId tenant, ControlPlaneSettingsSnapshot snapshot);
}
