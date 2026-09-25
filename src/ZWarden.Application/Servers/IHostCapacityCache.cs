using ZWarden.Domain.Ids;

namespace ZWarden.Application.Servers;

/// <summary>
/// The latest host memory budget per Agent (#230): transient, in memory, last report wins. Empty until the Agent's
/// first report after a Web restart, so a reader must cope with "unknown".
/// </summary>
public interface IHostCapacityCache
{
    /// <summary>Records an Agent's latest report.</summary>
    void Record(HostCapacity capacity);

    /// <summary>The latest report from <paramref name="agentId"/>, or <c>null</c> when none has arrived.</summary>
    HostCapacity? GetLatest(AgentId agentId);
}
