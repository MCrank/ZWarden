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
}
