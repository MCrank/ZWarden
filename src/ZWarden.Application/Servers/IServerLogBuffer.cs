using ZWarden.Domain.Ids;

namespace ZWarden.Application.Servers;

/// <summary>
/// The in-memory, bounded per-Server log tail (F27). Live logs are high-churn and transient, so they are never
/// persisted — the newest <c>N</c> lines per Server live here and are polled by the interactive log panel. Process-
/// local and a singleton, like the F16 metrics cache it sits beside. Storage is partitioned by <b>both</b> the
/// Server and the reporting Agent, so a batch forged by one Agent for a Server owned by another lands in its own
/// partition and never surfaces to a reader naming the true owner — the ownership guard (trust-boundaries.md §8),
/// stronger here than the metrics cache's last-writer model because a foreign write cannot even shadow the owner's.
/// </summary>
public interface IServerLogBuffer
{
    /// <summary>Appends the lines a reporting Agent streamed for a Server, evicting the oldest beyond the bound.
    /// <paramref name="dropped"/> latches the Server's sticky rate-cap indicator when the Agent coalesced lines.</summary>
    void Append(AgentId reportingAgentId, ServerId serverId, IReadOnlyList<ServerLogLineView> lines, bool dropped);

    /// <summary>Returns the buffered lines for a Server with a sequence greater than <paramref name="afterSequence"/>
    /// — but only from the partition reported by <paramref name="owningAgentId"/> (the Server's true owner); a
    /// reader naming any other Agent gets an empty slice. Pass <c>0</c> to read the whole retained tail.</summary>
    ServerLogSlice Read(ServerId serverId, AgentId owningAgentId, long afterSequence);
}
