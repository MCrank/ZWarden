using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Settings;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Settings;

/// <summary>
/// The in-process cache of each tenant's control-plane setting overrides (#345, ADR 0048), so the shell reads the
/// instance name on every page without a query. A miss loads in a fresh tenant scope, never the caller's own (a circuit
/// shares one <c>DbContext</c> across concurrent renders, the #154 class). The settings service calls
/// <see cref="Remember"/> after each save; a load racing a save never overwrites the saved value.
/// </summary>
public sealed class ControlPlaneSettingsCache : IControlPlaneSettingsCache
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ConcurrentDictionary<TenantId, ControlPlaneSettingsSnapshot> _entries = new();

    public ControlPlaneSettingsCache(IServiceScopeFactory scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        _scopes = scopes;
    }

    /// <inheritdoc />
    public async Task<ControlPlaneSettingsSnapshot> GetAsync(TenantId tenant, CancellationToken cancellationToken = default)
    {
        if (_entries.TryGetValue(tenant, out ControlPlaneSettingsSnapshot? cached))
        {
            return cached;
        }

        await using AsyncServiceScope scope = _scopes.CreateTenantScope(tenant);
        ControlPlaneSettingsSnapshot loaded = await scope.ServiceProvider
            .GetRequiredService<IControlPlaneSettingsService>()
            .GetAsync(cancellationToken).ConfigureAwait(false);
        return _entries.GetOrAdd(tenant, loaded);
    }

    /// <inheritdoc />
    public void Remember(TenantId tenant, ControlPlaneSettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _entries[tenant] = snapshot;
    }
}
