using Microsoft.Extensions.Options;
using ZWarden.Application.Settings;
using ZWarden.Application.Tenancy;

namespace ZWarden.Web.Configuration;

/// <summary>
/// The instance's display name as the shell shows it (#345, ADR 0048): the operator's override from Settings, else
/// <c>ZWarden:Instance:Name</c>. Reads the in-process settings cache, so it costs no query per page; with no tenant
/// (an anonymous page) it is always the configured name, so an override is never shown before sign-in.
/// </summary>
public sealed class InstanceName
{
    private readonly IOptions<InstanceOptions> _options;
    private readonly IControlPlaneSettingsCache _cache;
    private readonly ITenantContext _tenant;

    public InstanceName(IOptions<InstanceOptions> options, IControlPlaneSettingsCache cache, ITenantContext tenant)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(tenant);
        _options = options;
        _cache = cache;
        _tenant = tenant;
    }

    /// <summary>The configured name (<c>ZWarden:Instance:Name</c>, default "ZWarden"), which applies with no override.</summary>
    public string Configured => _options.Value.Name;

    /// <summary>The name to show: the tenant's override, else <see cref="Configured"/>.</summary>
    public async Task<string> GetAsync(CancellationToken cancellationToken = default)
    {
        if (!_tenant.HasCurrentTenant)
        {
            return Configured;
        }

        ControlPlaneSettingsSnapshot settings = await _cache.GetAsync(_tenant.CurrentTenantId, cancellationToken).ConfigureAwait(false);
        return settings.InstanceName ?? Configured;
    }
}
