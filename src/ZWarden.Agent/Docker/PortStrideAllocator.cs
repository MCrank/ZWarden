namespace ZWarden.Agent.Docker;

/// <summary>
/// Two-port-stride allocation for multi-server hosts (PRD 28, scope-and-sequencing §6). The canonical PZ
/// container always exposes 16261/udp + 16262/udp internally (F12); on the host, Server <c>n</c> publishes
/// them at the base pair plus a stride of two — stride 0 → 16261/16262, stride 1 → 16263/16264, and so on —
/// so two containers never collide on a host port. The lowest free stride is always chosen, so a freed
/// middle slot is reused before the range grows.
/// </summary>
public static class PortStrideAllocator
{
    /// <summary>The container-internal game/Steam UDP port, and stride 0's host port.</summary>
    public const ushort BaseGamePort = 16261;

    /// <summary>The container-internal direct UDP port, and stride 0's host port.</summary>
    public const ushort BaseDirectPort = 16262;

    /// <summary>The spacing between adjacent Servers' allocations: two ports.</summary>
    public const int Stride = 2;

    /// <summary>The host port pair for a given zero-based stride.</summary>
    public static PortAllocation ForStride(int stride)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(stride);
        int game = BaseGamePort + (stride * Stride);
        int direct = BaseDirectPort + (stride * Stride);
        if (direct > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(stride), stride, "Stride exceeds the addressable UDP port range.");
        }

        return new PortAllocation((ushort)game, (ushort)direct);
    }

    /// <summary>
    /// The stride a game port belongs to, or <c>-1</c> if it is not aligned to the base pair — e.g. a port
    /// that is not <c>BaseGamePort + 2n</c>. Used to read strides back off discovered containers' bindings.
    /// </summary>
    public static int StrideOfGamePort(ushort gamePort)
    {
        int offset = gamePort - BaseGamePort;
        return offset >= 0 && offset % Stride == 0 ? offset / Stride : -1;
    }

    /// <summary>
    /// Allocates the lowest free stride's port pair, given the allocations already in use on the host
    /// (read from discovery of existing canonical containers). Freed middle strides are reused first.
    /// </summary>
    public static PortAllocation AllocateNext(IEnumerable<PortAllocation> inUse)
    {
        ArgumentNullException.ThrowIfNull(inUse);

        HashSet<int> usedStrides = [];
        foreach (PortAllocation allocation in inUse)
        {
            int stride = StrideOfGamePort(allocation.GamePort);
            if (stride >= 0)
            {
                usedStrides.Add(stride);
            }
        }

        int next = 0;
        while (usedStrides.Contains(next))
        {
            next++;
        }

        return ForStride(next);
    }
}
