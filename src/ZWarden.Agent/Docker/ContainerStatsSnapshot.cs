namespace ZWarden.Agent.Docker;

/// <summary>
/// The raw counters a single non-streaming <c>docker stats</c> read yields (F16), projected out of the Docker
/// model so the CPU/memory maths (<see cref="ContainerStatsCalculator"/>) is a pure function testable without a
/// daemon. All fields are the engine's own cumulative counters; the calculator turns the CPU deltas and the
/// memory usage/limit into a percentage and byte figures.
/// </summary>
/// <param name="CpuTotalUsage">Cumulative container CPU usage, nanoseconds (<c>cpu_stats.cpu_usage.total_usage</c>).</param>
/// <param name="PreCpuTotalUsage">The same counter from the previous read (<c>precpu_stats</c>).</param>
/// <param name="SystemCpuUsage">Cumulative host CPU usage (<c>cpu_stats.system_cpu_usage</c>).</param>
/// <param name="PreSystemCpuUsage">The same host counter from the previous read.</param>
/// <param name="OnlineCpus">Logical CPUs available to the container (<c>cpu_stats.online_cpus</c>).</param>
/// <param name="MemoryUsage">Current memory usage in bytes (<c>memory_stats.usage</c>).</param>
/// <param name="MemoryCache">Reclaimable cache to subtract (cgroup v1 <c>cache</c> / v2 <c>inactive_file</c>).</param>
/// <param name="MemoryLimit">The container's memory limit in bytes (<c>memory_stats.limit</c>).</param>
public readonly record struct ContainerStatsSnapshot(
    ulong CpuTotalUsage,
    ulong PreCpuTotalUsage,
    ulong SystemCpuUsage,
    ulong PreSystemCpuUsage,
    uint OnlineCpus,
    ulong MemoryUsage,
    ulong MemoryCache,
    ulong MemoryLimit);
