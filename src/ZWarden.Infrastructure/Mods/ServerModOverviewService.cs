using Microsoft.Extensions.Options;
using ZWarden.Application.Authorization;
using ZWarden.Application.Mods;
using ZWarden.Application.Tenancy;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Servers;

namespace ZWarden.Infrastructure.Mods;

/// <summary>The <see cref="IServerModOverviewService"/> (#290): resolve through the tenant filter, authorize
/// <c>Mod.View</c>, then derive with <see cref="ModChangeSet"/>.</summary>
public sealed class ServerModOverviewService : IServerModOverviewService
{
    private readonly ServerRepository _servers;
    private readonly IPermissionChecker _permissions;
    private readonly ServerModStateRepository _states;
    private readonly ServerWorkshopItemRepository _items;
    private readonly IModRefreshScheduler _scheduler;
    private readonly ITenantContext _tenant;
    private readonly ModRefreshOptions _options;

    public ServerModOverviewService(
        ServerRepository servers,
        IPermissionChecker permissions,
        ServerModStateRepository states,
        ServerWorkshopItemRepository items,
        IModRefreshScheduler scheduler,
        ITenantContext tenant,
        IOptions<ModRefreshOptions> options)
    {
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(options);
        _servers = servers;
        _permissions = permissions;
        _states = states;
        _items = items;
        _scheduler = scheduler;
        _tenant = tenant;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<ServerModOverview?> GetAsync(UserId user, ServerId server, CancellationToken cancellationToken = default) =>
        await CanViewAsync(user, server, cancellationToken).ConfigureAwait(false)
            ? await DeriveAsync(server, cancellationToken).ConfigureAwait(false)
            : null;

    /// <inheritdoc />
    public async Task RequestUpdateCheckAsync(UserId user, ServerId server, CancellationToken cancellationToken = default)
    {
        if (await CanViewAsync(user, server, cancellationToken).ConfigureAwait(false))
        {
            _scheduler.Enqueue(new ModRefreshRequest(
                _tenant.CurrentTenantId, ModRefreshKind.RefreshMetadata, Server: server, MaxAge: _options.UpdateCheckMaxAge));
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<ServerId, int>> CountUpdatesReadyAsync(
        UserId user, IReadOnlyList<ServerId> servers, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(servers);
        Dictionary<ServerId, int> counts = [];
        foreach (ServerId server in servers.Distinct())
        {
            if (await CanViewAsync(user, server, cancellationToken).ConfigureAwait(false))
            {
                counts[server] = (await DeriveAsync(server, cancellationToken).ConfigureAwait(false)).UpdatesReady;
            }
        }

        return counts;
    }

    private async Task<bool> CanViewAsync(UserId user, ServerId server, CancellationToken cancellationToken)
    {
        if (await _servers.FindByIdAsync(server, cancellationToken).ConfigureAwait(false) is null)
        {
            return false;
        }

        AuthorizationDecision decision = await _permissions
            .EvaluateAsync(user, Permissions.ModView, server: server, cancellationToken).ConfigureAwait(false);
        return decision.IsAllowed;
    }

    private async Task<ServerModOverview> DeriveAsync(ServerId server, CancellationToken cancellationToken) =>
        ModChangeSet.Derive(
            server,
            await _states.FindAsync(server, cancellationToken).ConfigureAwait(false),
            await _items.ListForServerAsync(server, cancellationToken).ConfigureAwait(false));
}
