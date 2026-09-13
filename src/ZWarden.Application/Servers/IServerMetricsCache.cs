using ZWarden.Domain.Ids;

namespace ZWarden.Application.Servers;

/// <summary>
/// The in-memory, latest-sample-per-Server metrics store (F16). Metrics are high-churn and transient, so they
/// are never persisted — the newest sample per Server lives here and is pushed to the live UI. Process-local and
/// a singleton, like the F10 connection registry and the F14 discovery cache. Reads are <b>ownership-guarded</b>:
/// a sample is returned only to a caller that names the Server's true owning Agent, so a sample forged by another
/// Agent for an unguessable ServerId cannot surface under the wrong Server (trust-boundaries.md §8).
/// </summary>
public interface IServerMetricsCache
{
    /// <summary>Records the latest samples an Agent reported (last write per Server wins).</summary>
    void Record(IReadOnlyList<ServerMetrics> samples);

    /// <summary>The latest sample for a Server, but only if it was reported by <paramref name="owningAgentId"/>
    /// (the Server's true owner); otherwise <c>null</c>.</summary>
    ServerMetrics? GetLatest(ServerId serverId, AgentId owningAgentId);
}
