using ZWarden.Domain.Ids;

namespace ZWarden.Application.Agents;

/// <summary>
/// The read-only Host inventory the operator <c>/hosts</c> page reads (F35). It lists every trusted Agent in
/// the current tenant with its self-reported Host facts and observed connection state, overlaying the live
/// in-memory registry's "connected right now" over the persisted last-known state. Gated on the tenant-wide
/// <c>Agent.View</c> permission (D-2 — host-scoped narrowing is deferred to v1.1); read through the tenant
/// filter (ADR 0016). It never exposes a credential or its hash. Trust management (rotate, revoke, disable,
/// enable) stays on <see cref="IAgentTrustService"/>.
/// </summary>
public interface IAgentInventory
{
    /// <summary>Lists the Hosts in the current tenant for <paramref name="actor"/>, who must hold
    /// <c>Agent.View</c>.</summary>
    Task<IReadOnlyList<HostSummary>> ListHostsAsync(UserId actor, CancellationToken cancellationToken = default);
}
