namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// The Agent's periodic report of its host's memory budget (#230), sent on the metrics cadence. The new-server
/// wizard uses it to show how much RAM is free for another server and to warn before overcommitting. Observed,
/// untrusted data (trust-boundaries.md §3): ZWarden.Web keeps only the latest report per Agent in memory and uses it
/// for guidance, never for an authorization or safety decision.
/// </summary>
/// <param name="TotalMemoryBytes">The Docker host's total RAM (the daemon's <c>MemTotal</c>).</param>
/// <param name="CommittedMemoryBytes">The sum of the memory limits of every container this Agent manages, stopped
/// ones included (they will start again).</param>
/// <param name="MemoryOverheadBytes">The Agent's per-container overhead on top of the heap (limit = heap + this).</param>
/// <param name="DefaultHeapSizeBytes">The heap the Agent uses when a create names none.</param>
/// <param name="ReserveMemoryBytes">RAM the operator keeps back for the host OS and everything else.</param>
/// <param name="CpuPercent">#170: host-wide CPU busy % since the previous report; <c>null</c> when unknown (the first
/// report, a host without <c>/proc</c>, or an Agent built before #170).</param>
/// <param name="MemoryUsedBytes">#170: host RAM in use (<c>MemTotal − MemAvailable</c>); <c>null</c> when unknown.</param>
/// <param name="DiskFreeBytes">#170: free space the Agent can write on the volume holding the PZ data root.</param>
/// <param name="DiskTotalBytes">#170: the total size of that volume.</param>
[ProtocolMessage("agent.host-capacity")]
public sealed record HostCapacityReport(
    long TotalMemoryBytes,
    long CommittedMemoryBytes,
    long MemoryOverheadBytes,
    long DefaultHeapSizeBytes,
    long ReserveMemoryBytes,
    double? CpuPercent = null,
    long? MemoryUsedBytes = null,
    long? DiskFreeBytes = null,
    long? DiskTotalBytes = null) : AgentEvent;
