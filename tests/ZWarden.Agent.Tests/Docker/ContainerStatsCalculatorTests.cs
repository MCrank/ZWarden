using ZWarden.Agent.Docker;

namespace ZWarden.Agent.Tests.Docker;

/// <summary>
/// F16 PR-B: the pure docker-stats maths. CPU% is the container/host CPU-delta ratio scaled by CPU count, and
/// memory is usage minus reclaimable cache — both guarded against counter resets, first reads and divide-by-zero.
/// </summary>
public class ContainerStatsCalculatorTests
{
    [Test]
    public async Task Cpu_percent_is_the_delta_ratio_scaled_by_cpu_count()
    {
        // container used 20 of 100 host CPU-ns of delta, across 4 CPUs => 0.2 * 4 * 100 = 80%.
        ContainerStatsSnapshot s = new(
            CpuTotalUsage: 120, PreCpuTotalUsage: 100,
            SystemCpuUsage: 1_100, PreSystemCpuUsage: 1_000,
            OnlineCpus: 4,
            MemoryUsage: 0, MemoryCache: 0, MemoryLimit: 0);

        await Assert.That(ContainerStatsCalculator.CpuPercent(s)).IsEqualTo(80d).Within(0.0001);
    }

    [Test]
    public async Task Cpu_percent_is_zero_when_the_system_delta_is_zero()
    {
        // No host CPU delta between reads (e.g. the two samples coincide) => no ratio, report 0 rather than divide.
        ContainerStatsSnapshot s = new(120, 100, 1_000, 1_000, 4, 0, 0, 0);

        await Assert.That(ContainerStatsCalculator.CpuPercent(s)).IsEqualTo(0d);
    }

    [Test]
    public async Task Cpu_percent_is_zero_when_a_counter_resets_backwards()
    {
        ContainerStatsSnapshot s = new(50, 100, 900, 1_000, 4, 0, 0, 0);

        await Assert.That(ContainerStatsCalculator.CpuPercent(s)).IsEqualTo(0d);
    }

    [Test]
    public async Task Cpu_percent_defaults_to_one_cpu_when_online_cpus_is_zero()
    {
        ContainerStatsSnapshot s = new(120, 100, 1_100, 1_000, OnlineCpus: 0, 0, 0, 0);

        // 20/100 * 1 * 100 = 20%.
        await Assert.That(ContainerStatsCalculator.CpuPercent(s)).IsEqualTo(20d).Within(0.0001);
    }

    [Test]
    public async Task Memory_used_subtracts_reclaimable_cache_and_clamps_at_zero()
    {
        ContainerStatsSnapshot s = new(0, 0, 0, 0, 1,
            MemoryUsage: 3_000, MemoryCache: 1_200, MemoryLimit: 4_000);

        await Assert.That(ContainerStatsCalculator.MemoryUsedBytes(s)).IsEqualTo(1_800);
        await Assert.That(ContainerStatsCalculator.MemoryLimitBytes(s)).IsEqualTo(4_000);
    }

    [Test]
    public async Task Memory_used_never_goes_negative_when_cache_exceeds_usage()
    {
        ContainerStatsSnapshot s = new(0, 0, 0, 0, 1, MemoryUsage: 500, MemoryCache: 900, MemoryLimit: 4_000);

        await Assert.That(ContainerStatsCalculator.MemoryUsedBytes(s)).IsEqualTo(0);
    }
}
