using Microsoft.EntityFrameworkCore;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Servers;

/// <summary>
/// A tenant-scoped repository over <see cref="Server"/> (ADR 0016; trust-boundaries §9 rule 4). Every read
/// builds on the filtered query root, so no path returns another tenant's Servers — the inventory is, by
/// construction, tenant-scoped.
/// </summary>
public sealed class ServerRepository : TenantScopedRepository<Server>
{
    public ServerRepository(ZWardenDbContext context)
        : base(context)
    {
    }

    /// <summary>The ambient tenant's Server with the given id, or <c>null</c>.</summary>
    public async Task<Server?> FindByIdAsync(ServerId id, CancellationToken cancellationToken = default)
        => await Entities.FirstOrDefaultAsync(s => s.Id == id, cancellationToken).ConfigureAwait(false);

    /// <summary>The ambient tenant's Servers on the given Agent (host).</summary>
    public async Task<IReadOnlyList<Server>> ListByAgentAsync(
        AgentId agentId,
        CancellationToken cancellationToken = default)
        => await Entities.Where(s => s.AgentId == agentId).ToListAsync(cancellationToken).ConfigureAwait(false);
}
