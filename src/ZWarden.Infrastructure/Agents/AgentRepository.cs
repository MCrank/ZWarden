using Microsoft.EntityFrameworkCore;
using ZWarden.Domain.Agents;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Agents;

/// <summary>
/// A tenant-scoped repository over <see cref="Agent"/> (ADR 0016; trust-boundaries §9 rule 4). Every read
/// builds on the filtered query root, so no path returns another tenant's Agents — credential verification
/// is, by construction, tenant-scoped.
/// </summary>
public sealed class AgentRepository : TenantScopedRepository<Agent>
{
    public AgentRepository(ZWardenDbContext context)
        : base(context)
    {
    }

    /// <summary>The ambient tenant's Agent holding the given credential hash, or <c>null</c>.</summary>
    public async Task<Agent?> FindByCredentialHashAsync(
        string credentialHash,
        CancellationToken cancellationToken = default)
        => await Entities.FirstOrDefaultAsync(a => a.CredentialHash == credentialHash, cancellationToken)
            .ConfigureAwait(false);

    /// <summary>The ambient tenant's Agent with the given id, or <c>null</c>.</summary>
    public async Task<Agent?> FindByIdAsync(
        AgentId id,
        CancellationToken cancellationToken = default)
        => await Entities.FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// The ambient tenant's Agents recorded as <see cref="AgentConnectionState.Connected"/> but not seen since
    /// <paramref name="threshold"/> — the stale connections the monitor reconciles to disconnected (F10). An
    /// Agent that has never been seen (<c>LastSeenAt is null</c>) is not connected and is excluded. The enum is
    /// stored through a string value converter, which the providers will not translate inside this predicate,
    /// so the filter runs in memory over the tenant's Agents — acceptable at v1.0's small Agent count.
    /// </summary>
    public async Task<IReadOnlyList<Agent>> FindStaleConnectionsAsync(
        DateTimeOffset threshold,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Agent> all = await ListAsync(cancellationToken).ConfigureAwait(false);
        return all
            .Where(a => a.ConnectionState == AgentConnectionState.Connected
                && a.LastSeenAt is { } seen
                && seen < threshold)
            .ToList();
    }
}
