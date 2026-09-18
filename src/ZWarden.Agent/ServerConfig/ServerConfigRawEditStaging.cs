using System.Diagnostics.CodeAnalysis;
using ZWarden.Contracts.Protocol;

namespace ZWarden.Agent.ServerConfig;

/// <summary>
/// Holds the chunks of an operator-authored whole-file configuration edit that ZWarden.Web stages to the Agent
/// (F20c PR-D, ADR 0042) until the matching <c>ConfigApplyRaw</c> Operation runs and takes them. The file text is
/// too large for the 2 KB Operation command payload, so it rides its own transport channel
/// (<see cref="AgentHubProtocol.StageServerConfigRawEdit"/>) ahead of the Operation; this reassembles the chunks by
/// correlation id, transiently and bounded. Nothing here is persisted — a process restart drops staged edits and the
/// Operation then fails cleanly ("content not received").
/// </summary>
public interface IServerConfigRawEditStaging
{
    /// <summary>Accepts one staged chunk. Ignored (defensively, never thrown) if it declares an out-of-range chunk
    /// count or would exceed the concurrent-staging bound — a buggy or hostile sender cannot flood the buffer.</summary>
    void Accept(ServerConfigRawEditChunk chunk);

    /// <summary>Takes the fully reassembled raw text for <paramref name="correlationId"/> and removes it, or returns
    /// <see langword="false"/> when no complete, unexpired set is staged under that id (missing, still partial,
    /// expired, or undecodable).</summary>
    bool TryTake(string correlationId, [NotNullWhen(true)] out string? rawText);
}

/// <inheritdoc cref="IServerConfigRawEditStaging" />
public sealed class ServerConfigRawEditStaging : IServerConfigRawEditStaging
{
    /// <summary>The most edits that may be staged concurrently; a new correlation id beyond this is dropped so a
    /// flood of stages cannot grow the buffer without bound.</summary>
    public const int MaxConcurrentStaged = 16;

    /// <summary>How long a staged edit lives before it is swept — generous enough for an Operation to be dispatched
    /// and reach the Agent, short enough that an abandoned stage does not linger.</summary>
    public static readonly TimeSpan Expiry = TimeSpan.FromMinutes(5);

    private readonly Lock _gate = new();
    private readonly Dictionary<string, Pending> _pending = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    public ServerConfigRawEditStaging(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public void Accept(ServerConfigRawEditChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        if (string.IsNullOrEmpty(chunk.CorrelationId)
            || chunk.ChunkCount < 1 || chunk.ChunkCount > ServerConfigRawEditCodec.MaxChunkCount)
        {
            return;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        lock (_gate)
        {
            SweepExpired(now);
            if (!_pending.TryGetValue(chunk.CorrelationId, out Pending? pending))
            {
                if (_pending.Count >= MaxConcurrentStaged)
                {
                    return; // bounded: refuse a new stage rather than grow without limit
                }

                pending = new Pending(chunk.ChunkCount, now);
                _pending[chunk.CorrelationId] = pending;
            }

            pending.Add(chunk);
        }
    }

    /// <inheritdoc />
    public bool TryTake(string correlationId, [NotNullWhen(true)] out string? rawText)
    {
        rawText = null;
        if (string.IsNullOrEmpty(correlationId))
        {
            return false;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        lock (_gate)
        {
            SweepExpired(now);
            if (!_pending.TryGetValue(correlationId, out Pending? pending) || !pending.IsComplete)
            {
                return false;
            }

            _pending.Remove(correlationId);
            try
            {
                rawText = ServerConfigRawEditCodec.Decode(pending.OrderedChunks());
                return true;
            }
#pragma warning disable CA1031 // A corrupt staged set is a failed apply, not a thrown exception out of the taker.
            catch (FormatException)
            {
                rawText = null;
                return false;
            }
#pragma warning restore CA1031
        }
    }

    private void SweepExpired(DateTimeOffset now)
    {
        if (_pending.Count == 0)
        {
            return;
        }

        List<string>? expired = null;
        foreach ((string id, Pending pending) in _pending)
        {
            if (now - pending.CreatedAt > Expiry)
            {
                (expired ??= []).Add(id);
            }
        }

        if (expired is not null)
        {
            foreach (string id in expired)
            {
                _pending.Remove(id);
            }
        }
    }

    // One in-flight staged edit: its declared chunk count, the chunks collected so far (deduped by index), and when
    // it was first seen (for expiry).
    private sealed class Pending(int chunkCount, DateTimeOffset createdAt)
    {
        private readonly Dictionary<int, ServerConfigRawEditChunk> _chunks = [];

        public DateTimeOffset CreatedAt { get; } = createdAt;

        public bool IsComplete => _chunks.Count == chunkCount;

        public void Add(ServerConfigRawEditChunk chunk)
        {
            if (chunk.ChunkCount == chunkCount && chunk.ChunkIndex >= 0 && chunk.ChunkIndex < chunkCount)
            {
                _chunks.TryAdd(chunk.ChunkIndex, chunk);
            }
        }

        public IReadOnlyList<ServerConfigRawEditChunk> OrderedChunks() => [.. _chunks.Values];
    }
}
