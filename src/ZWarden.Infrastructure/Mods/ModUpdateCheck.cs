using Microsoft.Extensions.Options;
using ZWarden.Application.Mods;
using ZWarden.Application.Tenancy;
using ZWarden.Domain.Ids;

namespace ZWarden.Infrastructure.Mods;

/// <summary>
/// One pass of the mod-update check (#275 D4), driven hourly by <see cref="ModUpdateCheckService"/>. A server that just
/// sits running is never rediscovered, so nothing else would refresh its items' Steam <c>time_updated</c>. This queues
/// one metadata refresh, with the short <see cref="ModRefreshOptions.UpdateCheckMaxAge"/>, per Server in the current
/// tenant that has Workshop files on disk; <see cref="ModRefreshProcessor"/> then asks Steam only for items whose details
/// are older than that, in one batched call per Server.
/// </summary>
public sealed class ModUpdateCheck
{
    private readonly ServerWorkshopItemRepository _items;
    private readonly IModRefreshScheduler _scheduler;
    private readonly ITenantContext _tenant;
    private readonly ModRefreshOptions _options;

    public ModUpdateCheck(
        ServerWorkshopItemRepository items,
        IModRefreshScheduler scheduler,
        ITenantContext tenant,
        IOptions<ModRefreshOptions> options)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(options);
        _items = items;
        _scheduler = scheduler;
        _tenant = tenant;
        _options = options.Value;
    }

    /// <summary>Queues the refreshes and returns how many Servers were queued.</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ServerId> servers =
            await _items.ListServersWithFilesOnDiskAsync(cancellationToken).ConfigureAwait(false);
        foreach (ServerId server in servers)
        {
            _scheduler.Enqueue(new ModRefreshRequest(
                _tenant.CurrentTenantId, ModRefreshKind.RefreshMetadata, Server: server, MaxAge: _options.UpdateCheckMaxAge));
        }

        return servers.Count;
    }
}
