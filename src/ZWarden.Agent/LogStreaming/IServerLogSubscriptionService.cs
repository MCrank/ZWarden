using ZWarden.Domain.Ids;

namespace ZWarden.Agent.LogStreaming;

/// <summary>
/// Owns the Agent's on-demand live-log follows (F27): one follow task per Server that an operator is watching. A
/// follow is started when the first viewer subscribes (Web ref-counts viewers and calls
/// <c>StartServerLogStream</c>) and stopped when the last leaves — it is <b>not</b> an
/// Operation (ADR 0022) and <b>not</b> an <c>AgentCommand</c>: ephemeral, read-only, per-viewer streaming, so it
/// carries no lock, audit or lifecycle row (ADR 0030). Follows are deduped per Server, sanitize every line at the
/// source (PRD 38), batch to avoid a message storm, and rate-cap to bound a flooding server.
/// </summary>
public interface IServerLogSubscriptionService
{
    /// <summary>Begins following <paramref name="serverId"/>'s logs if not already, forwarding sanitized batches
    /// through <paramref name="emitter"/> (bound to the live connection). Idempotent: a second call for a Server
    /// already followed is a no-op, so it does not matter that Web only calls it for the first viewer.</summary>
    void Start(ServerId serverId, IServerLogEmitter emitter);

    /// <summary>Stops following <paramref name="serverId"/>'s logs and tears the follow down. A no-op when it is
    /// not being followed.</summary>
    Task StopAsync(ServerId serverId);

    /// <summary>Stops every active follow — used when the control-plane connection drops, so no stale follow lingers
    /// against a dead connection; Web re-subscribes its live viewers on reconnect.</summary>
    Task StopAllAsync();
}
