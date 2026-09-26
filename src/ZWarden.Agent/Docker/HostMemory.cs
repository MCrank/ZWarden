namespace ZWarden.Agent.Docker;

/// <summary>
/// The Docker host's memory budget as the Agent sees it (#230): the host's total RAM, and the memory limits already
/// committed to the containers this Agent owns (stopped ones included — they will start again).
/// </summary>
/// <param name="TotalBytes">The Docker host's total RAM.</param>
/// <param name="CommittedBytes">The sum of the owned containers' memory limits.</param>
public sealed record HostMemory(long TotalBytes, long CommittedBytes);
