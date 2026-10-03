using ZWarden.Domain.Ids;

namespace ZWarden.Application.Mods;

/// <summary>What a <see cref="ModRefreshRequest"/> asks the background refresh to do (#290).</summary>
public enum ModRefreshKind
{
    /// <summary>Enqueue a mod discovery for one Server.</summary>
    DiscoverServer,

    /// <summary>Enqueue a mod discovery for every Server an Agent owns (it just connected).</summary>
    DiscoverAgentServers,

    /// <summary>Refresh the Steam details of one Server's tracked Workshop items, in one batched call.</summary>
    RefreshMetadata,
}

/// <summary>A unit of background mod-refresh work, carrying the tenant it runs in (#290 D1).</summary>
/// <param name="Tenant">The tenant whose scope the work runs in.</param>
/// <param name="Kind">What to do.</param>
/// <param name="Server">The Server, for <see cref="ModRefreshKind.DiscoverServer"/> and
/// <see cref="ModRefreshKind.RefreshMetadata"/>.</param>
/// <param name="Agent">The Agent, for <see cref="ModRefreshKind.DiscoverAgentServers"/>.</param>
public sealed record ModRefreshRequest(TenantId Tenant, ModRefreshKind Kind, ServerId? Server = null, AgentId? Agent = null);

/// <summary>
/// The in-process scheduler that keeps mod data fresh without anyone clicking Refresh (#290 D1). Callers (the Agent hub)
/// only drop a request here and return; a hosted worker runs discovery and the Steam refresh in the request's tenant
/// scope, so Steam latency never sits inside an Agent call. Requests are best-effort: a full queue drops the oldest, a
/// duplicate of a request still waiting is ignored, and anything in flight is lost on a web restart (the next boot or
/// Agent reconnect re-requests it).
/// </summary>
public interface IModRefreshScheduler
{
    /// <summary>Queues <paramref name="request"/> now.</summary>
    void Enqueue(ModRefreshRequest request);

    /// <summary>Queues <paramref name="request"/> after <paramref name="delay"/> (the post-boot follow-up, D2).</summary>
    void EnqueueAfter(ModRefreshRequest request, TimeSpan delay);
}
