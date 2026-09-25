using System.Collections.Concurrent;
using ZWarden.Application.Servers;
using ZWarden.Domain.Ids;

namespace ZWarden.Infrastructure.Servers;

/// <summary>
/// The default <see cref="IServerLogBuffer"/> (F27): a process-local, bounded per-(Server, reporting Agent) tail.
/// A singleton beside <see cref="ServerMetricsCache"/>. Partitioning by both ids is the ownership guard — a batch
/// forged by a foreign Agent lands in its own partition and a reader naming the true owner never sees it, and
/// (unlike the metrics cache) cannot even be shadowed by it. Each partition is a fixed-size ring: appending past
/// <see cref="ServerLogBufferOptions.MaxLinesPerServer"/> evicts the oldest lines.
/// </summary>
public sealed class ServerLogBuffer : IServerLogBuffer
{
    private readonly int _maxLines;
    private readonly ConcurrentDictionary<(ServerId Server, AgentId Agent), Partition> _partitions = new();

    /// <summary>Creates the buffer with the configured per-partition bound.</summary>
    public ServerLogBuffer(ServerLogBufferOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _maxLines = options.MaxLinesPerServer > 0 ? options.MaxLinesPerServer : 1;
    }

    /// <inheritdoc />
    public void Append(AgentId reportingAgentId, ServerId serverId, IReadOnlyList<ServerLogLineView> lines, bool dropped)
    {
        ArgumentNullException.ThrowIfNull(lines);
        Partition partition = _partitions.GetOrAdd((serverId, reportingAgentId), _ => new Partition(_maxLines));
        partition.Append(lines, dropped);
    }

    /// <inheritdoc />
    public ServerLogSlice Read(ServerId serverId, AgentId owningAgentId, long afterSequence) =>
        _partitions.TryGetValue((serverId, owningAgentId), out Partition? partition)
            ? partition.ReadSince(afterSequence)
            : new ServerLogSlice([], Dropped: false);

    // One (Server, Agent) partition: a bounded FIFO of lines plus the sticky drop flag, under a lock so the
    // hub's append and the panel's read never see a torn buffer.
    //
    // #241: the read cursor is this partition's own sequence, assigned on append — never the Agent's. The Agent
    // numbers each follow from 1 again (a new viewer after the last one left, an Agent restart), so a cursor over
    // its numbers skipped every new line until the new follow caught up. And each follow — or a re-attach after a
    // container restart — replays the last N lines as its tail; those are dropped by their daemon timestamp: a line
    // older than the newest kept is a replay, and one at the newest timestamp is a replay only if it matches a line
    // already kept at that instant.
    private sealed class Partition(int maxLines)
    {
        private readonly Queue<ServerLogLineView> _lines = new();
        private readonly ReplayMark _stdout = new();
        private readonly ReplayMark _stderr = new();
        private readonly Lock _gate = new();
        private long _sequence;
        private bool _dropped;

        public void Append(IReadOnlyList<ServerLogLineView> lines, bool dropped)
        {
            lock (_gate)
            {
                if (dropped)
                {
                    _dropped = true;
                }

                foreach (ServerLogLineView line in lines)
                {
                    if (IsReplay(line))
                    {
                        continue;
                    }

                    _lines.Enqueue(line with { Sequence = ++_sequence });
                    while (_lines.Count > maxLines)
                    {
                        _lines.Dequeue();
                    }
                }
            }
        }

        public ServerLogSlice ReadSince(long afterSequence)
        {
            lock (_gate)
            {
                List<ServerLogLineView> slice = [];
                foreach (ServerLogLineView line in _lines)
                {
                    if (line.Sequence > afterSequence)
                    {
                        slice.Add(line);
                    }
                }

                return new ServerLogSlice(slice, _dropped);
            }
        }

        private bool IsReplay(ServerLogLineView line) =>
            (line.IsStderr ? _stderr : _stdout).IsReplay(line);
    }

    // The replay high-water mark for one stream. Per stream because the daemon timestamps stdout and stderr
    // independently, so the two can interleave slightly out of timestamp order; within one stream they cannot.
    // Records each kept line as it answers.
    private sealed class ReplayMark
    {
        private readonly HashSet<string> _atNewest = new(StringComparer.Ordinal);
        private DateTimeOffset _newest = DateTimeOffset.MinValue;

        public bool IsReplay(ServerLogLineView line)
        {
            if (line.Timestamp < _newest)
            {
                return true;
            }

            if (line.Timestamp > _newest)
            {
                _newest = line.Timestamp;
                _atNewest.Clear();
            }

            return !_atNewest.Add(line.Text);
        }
    }
}
