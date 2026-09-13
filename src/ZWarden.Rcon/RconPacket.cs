using System.Buffers.Binary;
using System.Text;

namespace ZWarden.Rcon;

/// <summary>
/// One Source RCON packet: a request the client sends, or a single response chunk PZ sends back.
/// The wire form is a 4-byte little-endian <c>size</c> (excluding itself), then <c>size</c> bytes
/// of <c>id</c> (4, LE), <c>type</c> (4, LE), the UTF-8 <c>body</c>, and two trailing NUL bytes
/// (the body's terminator plus an empty trailing string). Bodies are treated as ASCII-only
/// (research §7 quirk 11: PZ decodes request bodies with the platform default charset, so only the
/// ASCII subset is portable) though decoded as UTF-8, which PZ uses for response bodies in 42.20.2.
/// </summary>
internal readonly record struct RconPacket(int Id, int Type, string Body)
{
    /// <summary>Serializes this packet to its full on-wire frame, including the 4-byte length prefix.</summary>
    /// <exception cref="RconProtocolException">The body is large enough that the size field would
    /// exceed <see cref="RconConstants.MaxSizeField"/> - a request PZ could not frame.</exception>
    public byte[] Encode()
    {
        // ASCII is the portable subset (quirk 11); commands and the generated password are ASCII, so
        // UTF-8 and ASCII bytes coincide. Encode as UTF-8 for symmetry with PZ's response encoding.
        int bodyByteCount = Encoding.UTF8.GetByteCount(Body);
        int size = bodyByteCount + RconConstants.SizeFieldOverhead;
        if (size > RconConstants.MaxSizeField)
        {
            throw new RconProtocolException(
                $"Encoded packet size {size} exceeds the Source RCON maximum {RconConstants.MaxSizeField}; " +
                $"the body is {bodyByteCount} bytes but at most {RconConstants.MaxResponseChunkBodyBytes} fit.");
        }

        var frame = new byte[4 + size];
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(0, 4), size);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4, 4), Id);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(8, 4), Type);
        Encoding.UTF8.GetBytes(Body, frame.AsSpan(12, bodyByteCount));
        // The final two bytes are already 0 from the fresh array: body terminator + empty string.
        return frame;
    }

    /// <summary>
    /// Decodes the <c>size</c>-byte payload that follows the length prefix (i.e. id, type, body and
    /// the two NULs) into a packet. <paramref name="payload"/> must be exactly the <c>size</c> bytes;
    /// the caller reads the length prefix and this many bytes off the stream first.
    /// </summary>
    /// <exception cref="RconProtocolException">The payload is too short to hold id, type and the two
    /// NUL terminators.</exception>
    public static RconPacket DecodePayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < RconConstants.MinSizeField)
        {
            throw new RconProtocolException(
                $"RCON payload of {payload.Length} bytes is shorter than the {RconConstants.MinSizeField}-byte " +
                "minimum (id + type + two NUL terminators).");
        }

        int id = BinaryPrimitives.ReadInt32LittleEndian(payload[..4]);
        int type = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(4, 4));

        // The body is everything after id/type up to - but not including - the trailing NUL(s). PZ
        // writes exactly two, but trim all trailing NULs defensively so a stray terminator never
        // leaks into the decoded text.
        ReadOnlySpan<byte> bodyRegion = payload[8..];
        int end = bodyRegion.Length;
        while (end > 0 && bodyRegion[end - 1] == 0)
        {
            end--;
        }

        string body = end == 0 ? string.Empty : Encoding.UTF8.GetString(bodyRegion[..end]);
        return new RconPacket(id, type, body);
    }

    /// <summary>The body length in bytes as it sits on the wire (UTF-8). A response chunk shorter
    /// than <see cref="RconConstants.MaxResponseChunkBodyBytes"/> is the last one (research §7).</summary>
    public int BodyByteCount => Encoding.UTF8.GetByteCount(Body);
}
