using ZWarden.Domain.Ids;

namespace ZWarden.Application.Agents;

/// <summary>
/// #368: the in-memory, per-Agent record of the foreign containers each Agent's latest state snapshot reported. Like
/// <c>IServerDiscoveryCache</c> it is process-local and holds only "as of the last snapshot"; the entries are
/// untrusted.
/// </summary>
public interface IForeignContainerCache
{
    /// <summary>Replaces what <paramref name="reporter"/> reported with its latest snapshot.</summary>
    void Record(AgentId reporter, IReadOnlyList<ReportedForeignContainer> containers);

    /// <summary>What <paramref name="reporter"/> last reported, or empty.</summary>
    IReadOnlyList<ReportedForeignContainer> GetReported(AgentId reporter);

    /// <summary>Drops what <paramref name="reporter"/> reported (after a Replace, until its next snapshot).</summary>
    void Forget(AgentId reporter);
}
