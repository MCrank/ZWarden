using System.Buffers.Binary;
using System.Text;

namespace ZWarden.Rcon.Tests;

/// <summary>
/// The Source RCON wire codec, exercised directly as the executable spec of the framing in
/// research §7: a little-endian size that excludes itself, id and type, a UTF-8 body, and two
/// trailing NULs. These are the internal building block the connection reads and writes.
/// </summary>
public class RconPacketTests
{
    [Test]
    public async Task Encode_then_decode_round_trips_id_type_and_body()
    {
        var packet = new RconPacket(7, RconConstants.Type.ExecCommand, "players");

        byte[] frame = packet.Encode();
        // Strip the 4-byte length prefix; DecodePayload takes the size-counted region.
        RconPacket decoded = RconPacket.DecodePayload(frame.AsSpan(4).ToArray());

        await Assert.That(decoded.Id).IsEqualTo(7);
        await Assert.That(decoded.Type).IsEqualTo(RconConstants.Type.ExecCommand);
        await Assert.That(decoded.Body).IsEqualTo("players");
    }

    [Test]
    public async Task Encode_writes_a_size_field_that_excludes_the_length_prefix()
    {
        var packet = new RconPacket(1, RconConstants.Type.Auth, "abc");

        byte[] frame = packet.Encode();

        int size = BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(0, 4));
        // size = id(4) + type(4) + body(3) + two NULs(2) = 13; whole frame = 4 + 13 = 17.
        await Assert.That(size).IsEqualTo(13);
        await Assert.That(frame.Length).IsEqualTo(17);
        // The frame ends in two NUL bytes.
        await Assert.That(frame[^1]).IsEqualTo((byte)0);
        await Assert.That(frame[^2]).IsEqualTo((byte)0);
    }

    [Test]
    public async Task Encode_empty_body_produces_the_minimum_size_field()
    {
        var packet = new RconPacket(3, RconConstants.Type.ResponseValue, string.Empty);

        byte[] frame = packet.Encode();

        int size = BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(0, 4));
        await Assert.That(size).IsEqualTo(RconConstants.MinSizeField);
    }

    [Test]
    public async Task Encode_rejects_a_body_that_would_exceed_the_maximum_size_field()
    {
        // One byte past the largest body PZ could frame.
        var oversized = new RconPacket(1, RconConstants.Type.ExecCommand, new string('x', RconConstants.MaxResponseChunkBodyBytes + 1));

        await Assert.That(() => oversized.Encode()).Throws<RconProtocolException>();
    }

    [Test]
    public async Task Encode_accepts_a_body_at_exactly_the_maximum()
    {
        var maxed = new RconPacket(1, RconConstants.Type.ResponseValue, new string('x', RconConstants.MaxResponseChunkBodyBytes));

        byte[] frame = maxed.Encode();

        int size = BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(0, 4));
        await Assert.That(size).IsEqualTo(RconConstants.MaxSizeField);
    }

    [Test]
    public async Task DecodePayload_strips_trailing_nul_terminators()
    {
        // id=9, type=0, body "hi", then the two NULs - built by hand.
        byte[] payload = new byte[4 + 4 + 2 + 2];
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 9);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), 0);
        Encoding.UTF8.GetBytes("hi").CopyTo(payload.AsSpan(8));
        // last two bytes already 0

        RconPacket decoded = RconPacket.DecodePayload(payload);

        await Assert.That(decoded.Id).IsEqualTo(9);
        await Assert.That(decoded.Body).IsEqualTo("hi");
    }

    [Test]
    public async Task DecodePayload_rejects_a_payload_shorter_than_the_minimum()
    {
        // Only id + type, missing the two NUL terminators.
        byte[] tooShort = new byte[8];

        await Assert.That(() => RconPacket.DecodePayload(tooShort)).Throws<RconProtocolException>();
    }

    [Test]
    public async Task BodyByteCount_reports_the_utf8_byte_length()
    {
        var packet = new RconPacket(1, RconConstants.Type.ResponseValue, "abcd");

        await Assert.That(packet.BodyByteCount).IsEqualTo(4);
    }
}
