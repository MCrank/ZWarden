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
    /// The host port pair for an operator-chosen game port (#229): the game port and the port above it. The caller
    /// validates the range first (<c>HostPortRules.ValidateGamePort</c>).
    /// </summary>
    public static PortAllocation ForGamePort(ushort gamePort)
    {
        if (gamePort == ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(gamePort), gamePort, "The game port leaves no room for its direct port.");
        }

        return new PortAllocation(gamePort, (ushort)(gamePort + 1));
    }

    /// <summary>Whether neither port of the pair starting at <paramref name="gamePort"/> is already taken.</summary>
    public static bool IsPairFree(ushort gamePort, IReadOnlySet<ushort> occupiedHostPorts)
    {
        ArgumentNullException.ThrowIfNull(occupiedHostPorts);
        PortAllocation pair = ForGamePort(gamePort);
        return !occupiedHostPorts.Contains(pair.GamePort) && !occupiedHostPorts.Contains(pair.DirectPort);
    }

    /// <summary>
    /// Allocates the lowest stride whose pair is entirely free, given every host UDP port already published on the
    /// host (by any container, and including an operator's off-stride pair, #229). Freed middle strides are reused
    /// first.
    /// </summary>
    public static PortAllocation AllocateNext(IReadOnlySet<ushort> occupiedHostPorts)
    {
        ArgumentNullException.ThrowIfNull(occupiedHostPorts);

        for (int stride = 0; ; stride++)
        {
            PortAllocation candidate = ForStride(stride);
            if (IsPairFree(candidate.GamePort, occupiedHostPorts))
            {
                return candidate;
            }
        }
    }
}
