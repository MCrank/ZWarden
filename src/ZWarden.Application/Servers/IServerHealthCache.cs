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
/// <param name="Breakdown">The four probe verdicts behind the rollup, so the panel can show <i>which</i> probe
/// degraded the Server (F16, #93). Transient like the rest of this record — never persisted (ADR 0023).</param>
public sealed record ServerLiveHealth(
    AgentId AgentId,
    ServerId ServerId,
    ServerHealth Health,
    string Reason,
    DateTimeOffset ReportedAt,
    LiveHealthBreakdown Breakdown);

/// <summary>
/// The four probe verdicts behind a <see cref="ServerLiveHealth"/> rollup (F16, #93), the live-cache sibling of
/// the wire <c>HealthBreakdown</c> — container, process, startup, network. Application cannot reference Contracts,
/// so ZWarden.Web maps the wire breakdown onto this. Transient telemetry, never persisted.
/// </summary>
/// <param name="Container">Is the container running?</param>
/// <param name="Process">Is the game process alive and past its own HEALTHCHECK?</param>
/// <param name="Startup">Is the Server inside or past its startup window?</param>
/// <param name="Network">Are the published game/query UDP ports reachable on the host?</param>
public sealed record LiveHealthBreakdown(
    ProbeVerdict Container,
    ProbeVerdict Process,
    ProbeVerdict Startup,
    ProbeVerdict Network);

/// <summary>One probe's verdict within a <see cref="LiveHealthBreakdown"/> (F16, #93).</summary>
/// <param name="Status">The probe's outcome.</param>
/// <param name="Detail">An optional, <b>untrusted</b> human-readable note (trust-boundaries.md §8); may be null.</param>
public sealed record ProbeVerdict(ProbeStatus Status, string? Detail = null);

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
