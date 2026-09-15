using ZWarden.Domain.Ids;

namespace ZWarden.Application.Diagnostics;

/// <summary>The latest gathered checks for one Agent (host) or one Server, and when they were reported (F29).
/// Transient display data — the newest bundle per key lives in <see cref="IDiagnosticsResultCache"/>, never the
/// database (a run is transient, F29 D-2).</summary>
/// <param name="Checks">The mapped, domain-level checks the gather produced (untrusted detail, already bounded).</param>
/// <param name="ReportedAt">When the Agent reported the gather (UTC).</param>
public sealed record DiagnosticBundle(IReadOnlyList<DiagnosticCheck> Checks, DateTimeOffset ReportedAt);

/// <summary>
/// The in-memory, latest-gather-per-key store (F29), mirroring F16's health/metrics caches and F19's roster cache.
/// A gather bundle is transient display data — the newest one per Agent (host domains) and per Server (server
/// domains) lives here and feeds the tenant-wide and per-server reports, never the database. Process-local and a
/// singleton. Server reads are <b>ownership-guarded</b>: a bundle is returned only to a caller that names the
/// Server's true owning Agent, so a bundle reported for an unguessable ServerId cannot surface under the wrong
/// Server (trust-boundaries §8). Host reads are keyed by the reporting Agent itself.
/// </summary>
public interface IDiagnosticsResultCache
{
    /// <summary>Records the latest host-level bundle an Agent reported (last write per Agent wins).</summary>
    void RecordHost(AgentId agentId, DiagnosticBundle bundle);

    /// <summary>The latest host-level bundle for <paramref name="agentId"/>, or <c>null</c> if none.</summary>
    DiagnosticBundle? GetHost(AgentId agentId);

    /// <summary>Records the latest per-server bundle a Server reported, tagged with its owning Agent for the
    /// ownership guard (last write per Server wins).</summary>
    void RecordServer(ServerId serverId, AgentId owningAgentId, DiagnosticBundle bundle);

    /// <summary>The latest per-server bundle for <paramref name="serverId"/>, but only if it was reported by
    /// <paramref name="owningAgentId"/> (the Server's true owner); otherwise <c>null</c>.</summary>
    DiagnosticBundle? GetServer(ServerId serverId, AgentId owningAgentId);
}
