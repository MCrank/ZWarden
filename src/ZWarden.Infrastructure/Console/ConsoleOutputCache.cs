using System.Collections.Concurrent;
using ZWarden.Application.Console;
using ZWarden.Domain.Ids;

namespace ZWarden.Infrastructure.Console;

/// <summary>
/// The default <see cref="IConsoleOutputCache"/> (F28): a process-local store of the most recent console command
/// outputs per Server, bounded to <see cref="MaxEntriesPerServer"/>. A singleton, like F19's roster cache.
/// <see cref="GetSince"/> enforces the ownership guard — it returns entries only when the caller names the Agent
/// that reported them — and a process-monotonic sequence lets the live pane poll for what is new. Outputs are
/// transient display data, never persisted (the audit trail is the durable history — ADR 0032).
/// </summary>
public sealed class ConsoleOutputCache : IConsoleOutputCache
{
    /// <summary>The most recent outputs retained per Server for the live pane; older entries are dropped.</summary>
    public const int MaxEntriesPerServer = 100;

    private readonly ConcurrentDictionary<ServerId, ServerLog> _byServer = new();
    private long _sequence;

    /// <inheritdoc />
    public void Record(ServerId serverId, AgentId reportingAgentId, OperationId operationId, string output, bool truncated, DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(output);
        long sequence = Interlocked.Increment(ref _sequence);
        ConsoleOutputEntry entry = new(serverId, reportingAgentId, operationId, sequence, output, truncated, observedAt);
        ServerLog log = _byServer.GetOrAdd(serverId, static _ => new ServerLog());
        log.Add(entry);
    }

    /// <inheritdoc />
    public IReadOnlyList<ConsoleOutputEntry> GetSince(ServerId serverId, AgentId owningAgentId, long afterSequence)
    {
        if (!_byServer.TryGetValue(serverId, out ServerLog? log))
        {
            return [];
        }

        return log.Since(owningAgentId, afterSequence);
    }

    // A per-Server bounded, ordered buffer. All access is under its own lock — writes are rare (one per console
    // command) and reads are a short poll, so a lock is simpler and safer here than a lock-free ring.
    private sealed class ServerLog
    {
        private readonly object _gate = new();
        private readonly LinkedList<ConsoleOutputEntry> _entries = new();

        public void Add(ConsoleOutputEntry entry)
        {
            lock (_gate)
            {
                _entries.AddLast(entry);
                while (_entries.Count > MaxEntriesPerServer)
                {
                    _entries.RemoveFirst();
                }
            }
        }

        public List<ConsoleOutputEntry> Since(AgentId owningAgentId, long afterSequence)
        {
            lock (_gate)
            {
                List<ConsoleOutputEntry> matches = [];
                foreach (ConsoleOutputEntry entry in _entries)
                {
                    // Ownership guard (§8): only the true owning Agent's entries are visible, so output reported for
                    // an unguessable ServerId cannot surface under the wrong Server.
                    if (entry.AgentId == owningAgentId && entry.Sequence > afterSequence)
                    {
                        matches.Add(entry);
                    }
                }

                return matches;
            }
        }
    }
}
