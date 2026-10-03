using Microsoft.EntityFrameworkCore;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Mods;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Mods;

/// <summary>
/// The tenant-scoped repository over <see cref="ServerWorkshopItem"/> (#290). Reads only through the tenant filter
/// (ADR 0016); inserts are stamped with the ambient tenant by the ownership interceptor.
/// </summary>
public sealed class ServerWorkshopItemRepository : TenantScopedRepository<ServerWorkshopItem>
{
    public ServerWorkshopItemRepository(ZWardenDbContext context)
        : base(context)
    {
    }

    /// <summary>Every tracked item on <paramref name="server"/>, ordered by Workshop id.</summary>
    public async Task<IReadOnlyList<ServerWorkshopItem>> ListForServerAsync(ServerId server, CancellationToken cancellationToken = default)
        => await Entities
            .Where(i => i.ServerId == server)
            .OrderBy(i => i.WorkshopId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>The Servers that have at least one tracked item on disk (#275: the ones a restart could update).</summary>
    public async Task<IReadOnlyList<ServerId>> ListServersWithFilesOnDiskAsync(CancellationToken cancellationToken = default)
        => await Entities
            .Where(i => i.OnDisk)
            .Select(i => i.ServerId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>Stops tracking <paramref name="item"/>.</summary>
    public void Remove(ServerWorkshopItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Context.Remove(item);
    }
}
