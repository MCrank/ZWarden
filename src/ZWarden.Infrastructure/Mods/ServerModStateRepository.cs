using Microsoft.EntityFrameworkCore;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Mods;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Mods;

/// <summary>
/// The tenant-scoped repository over <see cref="ServerModState"/> (#290), one row per Server. Reads only through
/// the tenant filter (ADR 0016); inserts are stamped with the ambient tenant by the ownership interceptor.
/// </summary>
public sealed class ServerModStateRepository : TenantScopedRepository<ServerModState>
{
    public ServerModStateRepository(ZWardenDbContext context)
        : base(context)
    {
    }

    /// <summary>The state for <paramref name="server"/>, or <c>null</c> if none has been recorded (or it belongs to
    /// another tenant).</summary>
    public async Task<ServerModState?> FindAsync(ServerId server, CancellationToken cancellationToken = default)
        => await Entities
            .FirstOrDefaultAsync(s => s.ServerId == server, cancellationToken)
            .ConfigureAwait(false);
}
