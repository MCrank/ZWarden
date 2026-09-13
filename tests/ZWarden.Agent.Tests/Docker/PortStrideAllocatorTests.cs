using ZWarden.Agent.Docker;

namespace ZWarden.Agent.Tests.Docker;

/// <summary>
/// F13 S4: two-port-stride allocation (PRD 28). Server n publishes 16261/16262 at a stride of two; the lowest
/// free stride is always chosen so a freed middle slot is reused before the range grows.
/// </summary>
public class PortStrideAllocatorTests
{
    [Test]
    public async Task Stride_zero_is_the_base_pair()
    {
        PortAllocation zero = PortStrideAllocator.ForStride(0);

        await Assert.That(zero.GamePort).IsEqualTo((ushort)16261);
        await Assert.That(zero.DirectPort).IsEqualTo((ushort)16262);
    }

    [Test]
    public async Task Stride_one_is_two_ports_up()
    {
        PortAllocation one = PortStrideAllocator.ForStride(1);

        await Assert.That(one.GamePort).IsEqualTo((ushort)16263);
        await Assert.That(one.DirectPort).IsEqualTo((ushort)16264);
    }

    [Test]
    public async Task The_first_allocation_on_an_empty_host_is_stride_zero()
    {
        PortAllocation next = PortStrideAllocator.AllocateNext([]);

        await Assert.That(next).IsEqualTo(PortStrideAllocator.ForStride(0));
    }

    [Test]
    public async Task Contiguous_use_allocates_the_next_stride_up()
    {
        PortAllocation next = PortStrideAllocator.AllocateNext(
            [PortStrideAllocator.ForStride(0), PortStrideAllocator.ForStride(1)]);

        await Assert.That(next).IsEqualTo(PortStrideAllocator.ForStride(2));
        await Assert.That(next.GamePort).IsEqualTo((ushort)16265);
    }

    [Test]
    public async Task A_freed_middle_stride_is_reused_before_the_range_grows()
    {
        PortAllocation next = PortStrideAllocator.AllocateNext(
            [PortStrideAllocator.ForStride(0), PortStrideAllocator.ForStride(2)]);

        await Assert.That(next).IsEqualTo(PortStrideAllocator.ForStride(1));
        await Assert.That(next.GamePort).IsEqualTo((ushort)16263);
    }

    [Test]
    public async Task A_game_port_maps_back_to_its_stride()
    {
        await Assert.That(PortStrideAllocator.StrideOfGamePort(16261)).IsEqualTo(0);
        await Assert.That(PortStrideAllocator.StrideOfGamePort(16265)).IsEqualTo(2);
    }

    [Test]
    public async Task A_misaligned_port_has_no_stride()
    {
        // A direct port (odd offset) or a port below the base is not a game-port stride.
        await Assert.That(PortStrideAllocator.StrideOfGamePort(16262)).IsEqualTo(-1);
        await Assert.That(PortStrideAllocator.StrideOfGamePort(16000)).IsEqualTo(-1);
    }

    [Test]
    public async Task A_negative_stride_is_rejected()
    {
        await Assert.That(() => PortStrideAllocator.ForStride(-1)).Throws<ArgumentOutOfRangeException>();
    }
}
