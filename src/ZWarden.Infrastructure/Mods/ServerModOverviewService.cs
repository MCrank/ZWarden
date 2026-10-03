using ZWarden.Application.Authorization;
using ZWarden.Application.Mods;
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

    public ServerModOverviewService(
        ServerRepository servers,
        IPermissionChecker permissions,
        ServerModStateRepository states,
        ServerWorkshopItemRepository items)
    {
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(items);
        _servers = servers;
        _permissions = permissions;
        _states = states;
        _items = items;
    }

    /// <inheritdoc />
    public async Task<ServerModOverview?> GetAsync(UserId user, ServerId server, CancellationToken cancellationToken = default)
    {
        if (await _servers.FindByIdAsync(server, cancellationToken).ConfigureAwait(false) is null)
        {
            return null;
        }

        AuthorizationDecision decision = await _permissions
            .EvaluateAsync(user, Permissions.ModView, server: server, cancellationToken).ConfigureAwait(false);
        if (!decision.IsAllowed)
        {
            return null;
        }

        return ModChangeSet.Derive(
            server,
            await _states.FindAsync(server, cancellationToken).ConfigureAwait(false),
            await _items.ListForServerAsync(server, cancellationToken).ConfigureAwait(false));
    }
}
