using System.Text;

namespace ZWarden.Contracts.Protocol;

/// <summary>
/// Chunks an operator-authored whole-file configuration edit for the trip <b>up</b> to the Agent, and reassembles
/// it there (F20c PR-D, ADR 0042) — the write-direction sibling of <see cref="ServerConfigContentCodec"/>. The raw
/// text is UTF-8-then-Base64 encoded (so a chunk boundary can never split a multi-byte character or a UTF-16
/// surrogate pair — Base64 is ASCII and splits anywhere) and sliced into chunks no larger than
/// <see cref="DefaultMaxChunkChars"/>, comfortably under SignalR's default 32 KB per-message ceiling. Reassembly
/// validates the set is complete and consistent and bounds it at <see cref="MaxChunkCount"/> so a buggy or hostile
/// sender cannot flood the Agent's staging buffer. Both ends share this one codec.
/// </summary>
public static class ServerConfigRawEditCodec
{
    /// <summary>The largest chunk body ZWarden splits into — under SignalR's 32 KB default with envelope
    /// overhead to spare.</summary>
    public const int DefaultMaxChunkChars = 24_000;

    /// <summary>The most chunks one staged edit may carry; reassembling a set larger than this is refused so a
    /// sender cannot storm the staging buffer. At <see cref="DefaultMaxChunkChars"/> this bounds a raw edit near
    /// 1.1 MB of text — ample for a config file, far short of a flood.</summary>
    public const int MaxChunkCount = 64;

    /// <summary>Splits <paramref name="rawText"/> into ordered chunks tagged with <paramref name="correlationId"/>.
    /// An empty body still yields one chunk, so a stage is always at least one send.</summary>
    public static IReadOnlyList<ServerConfigRawEditChunk> Encode(
        string correlationId, string rawText, int maxChunkChars = DefaultMaxChunkChars)
    {
        ArgumentException.ThrowIfNullOrEmpty(correlationId);
        ArgumentNullException.ThrowIfNull(rawText);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxChunkChars, 1);

        string body = Convert.ToBase64String(Encoding.UTF8.GetBytes(rawText));

        int chunkCount = Math.Max(1, (body.Length + maxChunkChars - 1) / maxChunkChars);
        var chunks = new List<ServerConfigRawEditChunk>(chunkCount);
        for (int index = 0; index < chunkCount; index++)
        {
            int start = index * maxChunkChars;
            string slice = start >= body.Length
                ? string.Empty
                : body.Substring(start, Math.Min(maxChunkChars, body.Length - start));
            chunks.Add(new ServerConfigRawEditChunk(correlationId, index, chunkCount, slice));
        }

        return chunks;
    }

    /// <summary>
    /// Reassembles a complete chunk set (in any arrival order) back into the raw text. Throws
    /// <see cref="FormatException"/> if the set is empty, disagrees on its count or correlation id, exceeds
    /// <see cref="MaxChunkCount"/>, is missing or duplicating a chunk, or is not valid Base64. The Agent turns that
    /// into a failed apply rather than propagating it.
    /// </summary>
    public static string Decode(IReadOnlyList<ServerConfigRawEditChunk> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        if (chunks.Count == 0)
        {
            throw new FormatException("A staged configuration edit carried no chunks.");
        }

        int count = chunks[0].ChunkCount;
        if (count < 1 || count > MaxChunkCount)
        {
            throw new FormatException($"A staged configuration edit declared an out-of-range chunk count ({count}).");
        }

        if (chunks.Count != count)
        {
            throw new FormatException(
                $"A staged configuration edit expected {count} chunk(s) but {chunks.Count} were collected.");
        }

        string correlationId = chunks[0].CorrelationId;
        var ordered = new string?[count];
        foreach (ServerConfigRawEditChunk chunk in chunks)
        {
            if (chunk.ChunkCount != count || !string.Equals(chunk.CorrelationId, correlationId, StringComparison.Ordinal))
            {
                throw new FormatException("A staged configuration edit mixed inconsistent chunk headers.");
            }

            if (chunk.ChunkIndex < 0 || chunk.ChunkIndex >= count)
            {
                throw new FormatException($"A staged configuration edit carried an out-of-range chunk index ({chunk.ChunkIndex}).");
            }

            if (ordered[chunk.ChunkIndex] is not null)
            {
                throw new FormatException($"A staged configuration edit duplicated chunk index {chunk.ChunkIndex}.");
            }

            ordered[chunk.ChunkIndex] = chunk.Chunk;
        }

        var builder = new StringBuilder();
        foreach (string? slice in ordered)
        {
            builder.Append(slice ?? throw new FormatException("A staged configuration edit was missing a chunk."));
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(builder.ToString());
        }
        catch (FormatException ex)
        {
            throw new FormatException("A staged configuration edit body was not valid Base64.", ex);
        }

        return Encoding.UTF8.GetString(bytes);
    }
}
