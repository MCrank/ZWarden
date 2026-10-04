using ZWarden.Domain.Ids;

namespace ZWarden.Application.Servers;

/// <summary>
/// An Agent's latest reported host memory budget (#230), as the new-server wizard uses it. It mirrors the wire
/// <c>HostCapacityReport</c> without the Application layer depending on the Contracts assembly. <b>Observed, not
/// trusted</b> (trust-boundaries.md §3): it drives guidance and an operator acknowledgement, never an authorization.
/// </summary>
/// <param name="AgentId">The Agent that reported it (the authenticated connection, never the payload).</param>
/// <param name="TotalBytes">The Docker host's total RAM.</param>
/// <param name="CommittedBytes">The memory limits already committed to the Agent's containers, stopped ones included.</param>
/// <param name="OverheadBytes">The Agent's per-container overhead on top of the heap.</param>
/// <param name="DefaultHeapBytes">The heap the Agent uses when none is chosen.</param>
/// <param name="ReserveBytes">RAM kept back for the host OS.</param>
/// <param name="ReportedAt">When the Agent sent it.</param>
/// <param name="Vitals">#170: the host's live CPU / memory / disk for the Hosts card; <c>null</c> from an older Agent.</param>
public sealed record HostCapacity(
    AgentId AgentId,
    long TotalBytes,
    long CommittedBytes,
    long OverheadBytes,
    long DefaultHeapBytes,
    long ReserveBytes,
    DateTimeOffset ReportedAt,
    HostVitals? Vitals = null)
{
    /// <summary>RAM free for new servers: total − committed − reserve, never below zero.</summary>
    public long FreeBytes => Math.Max(0, TotalBytes - CommittedBytes - ReserveBytes);

    /// <summary>The container limit a server with <paramref name="heapBytes"/> would commit: heap + overhead.</summary>
    public long LimitFor(long heapBytes) => heapBytes + OverheadBytes;

    /// <summary>How far a server with <paramref name="heapBytes"/> would overshoot <see cref="FreeBytes"/>; 0 when it fits.</summary>
    public long ShortfallFor(long heapBytes) => Math.Max(0, LimitFor(heapBytes) - FreeBytes);

    /// <summary>RAM free for an existing server being resized: <see cref="FreeBytes"/> with its current limit (from
    /// <paramref name="currentHeapBytes"/>, or the default heap when unknown) counted as released.</summary>
    public long FreeForResize(long? currentHeapBytes) =>
        Math.Max(0, TotalBytes - Math.Max(0, CommittedBytes - LimitFor(currentHeapBytes ?? DefaultHeapBytes)) - ReserveBytes);

    /// <summary>How far raising a server's heap from <paramref name="currentHeapBytes"/> to <paramref name="newHeapBytes"/>
    /// would overshoot <see cref="FreeForResize"/>; 0 when it fits, and always 0 when the heap is kept or lowered.</summary>
    public long ResizeShortfall(long? currentHeapBytes, long newHeapBytes) =>
        newHeapBytes <= (currentHeapBytes ?? DefaultHeapBytes)
            ? 0
            : Math.Max(0, LimitFor(newHeapBytes) - FreeForResize(currentHeapBytes));
}
