using ZWarden.Domain.Ids;

namespace ZWarden.Application.Console;

/// <summary>
/// One remote-console command's observed output (F28) — the reply a successful <c>ExecuteConsoleCommand</c>
/// Operation returned over RCON. Transient display data, like F16's metrics/health and F19's roster: it lives in
/// the in-memory <see cref="IConsoleOutputCache"/> and feeds the live console pane, never the database (the audit
/// trail is the durable history — ADR 0032). The <see cref="Output"/> is <b>untrusted</b> PZ text
/// (trust-boundaries.md §8), carried verbatim for escaping at render, and bounded at the Agent
/// (<see cref="Truncated"/>).
/// </summary>
/// <param name="ServerId">The Server the command ran against.</param>
/// <param name="AgentId">The Agent that reported it — the cache ownership-guard key.</param>
/// <param name="OperationId">The console Operation this output belongs to (the join key to the submitted command).</param>
/// <param name="Sequence">A process-monotonic sequence, so the live pane can poll for entries after a cursor.</param>
/// <param name="Output">PZ's reply text (bounded, untrusted), possibly empty.</param>
/// <param name="Truncated">True when the Agent cut the reply to its output cap.</param>
/// <param name="ObservedAt">When the Agent observed the reply (UTC).</param>
public sealed record ConsoleOutputEntry(
    ServerId ServerId,
    AgentId AgentId,
    OperationId OperationId,
    long Sequence,
    string Output,
    bool Truncated,
    DateTimeOffset ObservedAt);

/// <summary>
/// The in-memory, bounded, latest-N-outputs-per-Server store (F28), mirroring F19's roster cache. A console
/// command's output is transient display data — the most recent entries per Server live here and feed the live UI
/// pane. Process-local and a singleton. Reads are <b>ownership-guarded</b>: entries are returned only to a caller
/// that names the Server's true owning Agent, so output reported for an unguessable ServerId cannot surface under
/// the wrong Server (trust-boundaries.md §8).
/// </summary>
public interface IConsoleOutputCache
{
    /// <summary>Records one console command's observed output, assigning it the next process sequence. The oldest
    /// entries beyond the per-Server bound are dropped.</summary>
    void Record(ServerId serverId, AgentId reportingAgentId, OperationId operationId, string output, bool truncated, DateTimeOffset observedAt);

    /// <summary>The retained entries for a Server with a <see cref="ConsoleOutputEntry.Sequence"/> greater than
    /// <paramref name="afterSequence"/>, oldest first — but only those reported by <paramref name="owningAgentId"/>
    /// (the Server's true owner); otherwise empty.</summary>
    IReadOnlyList<ConsoleOutputEntry> GetSince(ServerId serverId, AgentId owningAgentId, long afterSequence);
}
