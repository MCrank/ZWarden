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
    private sealed class Partition(int maxLines)
    {
        private readonly Queue<ServerLogLineView> _lines = new();
        private readonly Lock _gate = new();
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
                    _lines.Enqueue(line);
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
    }
}
