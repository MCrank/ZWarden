using System.Text;
using System.Text.Json;
using ZWarden.Contracts.Protocol.Messages;

namespace ZWarden.Contracts.Protocol;

/// <summary>
/// Encodes a <see cref="ConfigReadPayload"/> into, and decodes it back from, the sequenced
/// <see cref="ServerConfigContent"/> chunks the live-read reply rides in (F20c, ADR 0041). The payload is
/// serialized to canonical JSON with <see cref="ProtocolJson.Options"/>, Base64-encoded (so a chunk boundary can
/// never fall inside a multi-byte character or a UTF-16 surrogate pair — Base64 is ASCII and splits anywhere), and
/// sliced into chunks no larger than <see cref="DefaultMaxChunkChars"/>, comfortably under SignalR's default 32 KB
/// per-message ceiling. Decoding validates the chunk set is complete and consistent and bounds it at
/// <see cref="MaxChunkCount"/> so a compromised Agent cannot flood the reassembly buffer. Both ends share this one
/// codec, so a reply written by the Agent is read identically on ZWarden.Web.
/// </summary>
public static class ServerConfigContentCodec
{
    /// <summary>The largest chunk body ZWarden splits into — under SignalR's 32 KB default with envelope
    /// overhead to spare.</summary>
    public const int DefaultMaxChunkChars = 24_000;

    /// <summary>The most chunks a single reply may carry; decoding a set larger than this is refused so a
    /// compromised Agent cannot storm the reassembly buffer (ADR 0041).</summary>
    public const int MaxChunkCount = 64;

    /// <summary>Splits <paramref name="payload"/> into ordered chunks tagged with <paramref name="correlationId"/>.
    /// An empty body still yields one chunk, so a reply is always at least one send.</summary>
    public static IReadOnlyList<ServerConfigContent> Encode(
        string correlationId, ConfigReadPayload payload, int maxChunkChars = DefaultMaxChunkChars)
    {
        ArgumentException.ThrowIfNullOrEmpty(correlationId);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxChunkChars, 1);

        string json = JsonSerializer.Serialize(payload, ProtocolJson.Options);
        string body = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        int chunkCount = Math.Max(1, (body.Length + maxChunkChars - 1) / maxChunkChars);
        var chunks = new List<ServerConfigContent>(chunkCount);
        for (int index = 0; index < chunkCount; index++)
        {
            int start = index * maxChunkChars;
            string slice = start >= body.Length
                ? string.Empty
                : body.Substring(start, Math.Min(maxChunkChars, body.Length - start));
            chunks.Add(new ServerConfigContent(correlationId, index, chunkCount, slice));
        }

        return chunks;
    }

    /// <summary>
    /// Reassembles a complete chunk set (in any arrival order) back into its <see cref="ConfigReadPayload"/>. Throws
    /// <see cref="FormatException"/> if the set is empty, disagrees on its count or correlation id, exceeds
    /// <see cref="MaxChunkCount"/>, or is missing or duplicating a chunk; throws <see cref="JsonException"/> if the
    /// reassembled body is not a valid payload. The caller turns either into an operator-facing read failure rather
    /// than propagating it.
    /// </summary>
    public static ConfigReadPayload Decode(IReadOnlyList<ServerConfigContent> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        if (chunks.Count == 0)
        {
            throw new FormatException("A configuration read reply carried no chunks.");
        }

        int count = chunks[0].ChunkCount;
        if (count < 1 || count > MaxChunkCount)
        {
            throw new FormatException($"A configuration read reply declared an out-of-range chunk count ({count}).");
        }

        if (chunks.Count != count)
        {
            throw new FormatException(
                $"A configuration read reply expected {count} chunk(s) but {chunks.Count} were collected.");
        }

        string correlationId = chunks[0].CorrelationId;
        var ordered = new string?[count];
        foreach (ServerConfigContent chunk in chunks)
        {
            if (chunk.ChunkCount != count || !string.Equals(chunk.CorrelationId, correlationId, StringComparison.Ordinal))
            {
                throw new FormatException("A configuration read reply mixed inconsistent chunk headers.");
            }

            if (chunk.ChunkIndex < 0 || chunk.ChunkIndex >= count)
            {
                throw new FormatException($"A configuration read reply carried an out-of-range chunk index ({chunk.ChunkIndex}).");
            }

            if (ordered[chunk.ChunkIndex] is not null)
            {
                throw new FormatException($"A configuration read reply duplicated chunk index {chunk.ChunkIndex}.");
            }

            ordered[chunk.ChunkIndex] = chunk.Chunk;
        }

        var builder = new StringBuilder();
        foreach (string? slice in ordered)
        {
            builder.Append(slice ?? throw new FormatException("A configuration read reply was missing a chunk."));
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(builder.ToString());
        }
        catch (FormatException ex)
        {
            throw new FormatException("A configuration read reply body was not valid Base64.", ex);
        }

        string json = Encoding.UTF8.GetString(bytes);
        return JsonSerializer.Deserialize<ConfigReadPayload>(json, ProtocolJson.Options)
            ?? throw new JsonException("A configuration read reply body deserialized to null.");
    }
}
