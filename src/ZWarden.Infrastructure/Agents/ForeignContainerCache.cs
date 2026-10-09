using System.Collections.Concurrent;
using ZWarden.Application.Agents;
using ZWarden.Domain.Ids;

namespace ZWarden.Infrastructure.Agents;

/// <summary>
/// The process-local <see cref="IForeignContainerCache"/> (#368): each Agent's latest report of foreign-stamped
/// containers, rebuilt from the next snapshot after a restart. Registered as a singleton.
/// </summary>
public sealed class ForeignContainerCache : IForeignContainerCache
{
    private readonly ConcurrentDictionary<AgentId, IReadOnlyList<ReportedForeignContainer>> _byAgent = new();

    /// <inheritdoc />
    public void Record(AgentId reporter, IReadOnlyList<ReportedForeignContainer> containers)
    {
        ArgumentNullException.ThrowIfNull(containers);
        if (containers.Count == 0)
        {
            _byAgent.TryRemove(reporter, out _);
            return;
        }

        _byAgent[reporter] = [.. containers];
    }

    /// <inheritdoc />
    public IReadOnlyList<ReportedForeignContainer> GetReported(AgentId reporter) =>
        _byAgent.TryGetValue(reporter, out IReadOnlyList<ReportedForeignContainer>? containers) ? containers : [];

    /// <inheritdoc />
    public void Forget(AgentId reporter) => _byAgent.TryRemove(reporter, out _);
}
