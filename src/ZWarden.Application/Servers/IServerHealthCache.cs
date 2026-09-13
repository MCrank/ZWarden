using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;

namespace ZWarden.Application.Servers;

/// <summary>
/// The latest live health rollup ZWarden.Web holds for a Server (F16), mapped from a <c>HealthChanged</c> report
/// by ZWarden.Web. It exists so an interactive circuit can show <b>live</b> health without a tenant-scoped
/// database read (the tenant context needs an HttpContext, which a circuit lacks). Transient, non-secret; the
/// durable value is <c>Server.LastHealth</c>.
/// </summary>
/// <param name="AgentId">The Agent that reported the rollup (the ownership-guard key).</param>
/// <param name="ServerId">The Server the rollup is for.</param>
/// <param name="Health">The rolled-up health.</param>
/// <param name="Reason">A short, untrusted human-readable summary (trust-boundaries.md §8).</param>
/// <param name="ReportedAt">When the Agent reported it (UTC).</param>
public sealed record ServerLiveHealth(
    AgentId AgentId,
    ServerId ServerId,
    ServerHealth Health,
    string Reason,
    DateTimeOffset ReportedAt);

/// <summary>
/// The in-memory latest-health-per-Server store (F16): a singleton beside <see cref="IServerMetricsCache"/>,
/// filled from <c>HealthChanged</c> reports and read by the live per-server panel. Reads are
/// <b>ownership-guarded</b> exactly as the metrics cache — returned only to a caller naming the Server's true
/// owning Agent (trust-boundaries.md §8).
/// </summary>
public interface IServerHealthCache
{
    /// <summary>Records the latest health a Server reported (last write wins).</summary>
    void Record(ServerLiveHealth health);

    /// <summary>The latest health for a Server, but only if reported by <paramref name="owningAgentId"/>; else null.</summary>
    ServerLiveHealth? GetLatest(ServerId serverId, AgentId owningAgentId);
}
