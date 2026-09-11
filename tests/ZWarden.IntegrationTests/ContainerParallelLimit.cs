using TUnit.Core.Interfaces;

namespace ZWarden.IntegrationTests;

/// <summary>
/// The container-backed parallelism cap (Q4). Set to 1 in Feature 0 so the DB tier is
/// deterministic before PRD 21's per-Server-locking tests exist; raise it later only on
/// measurement. Applied with [ParallelLimiter&lt;ContainerParallelLimit&gt;].
/// </summary>
public sealed class ContainerParallelLimit : IParallelLimit
{
    public int Limit => 1;
}
