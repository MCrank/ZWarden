using ZWarden.Application.Servers;

namespace ZWarden.Infrastructure.Tests.Servers;

/// <summary>
/// #170: the host vitals the Hosts card meters show are untrusted Agent data, so ingest keeps only figures that make
/// sense — a CPU share outside 0–100, a negative byte count, more memory in use than the host has, or more free disk
/// than the volume holds is dropped (that figure, not the whole report).
/// </summary>
public class HostVitalsTests
{
    private const long GiB = 1024L * 1024 * 1024;

    [Test]
    public async Task Sensible_figures_are_kept()
    {
        HostVitals? vitals = HostVitals.Observed(42.5, 12 * GiB, 32 * GiB, 200 * GiB, 480 * GiB);

        await Assert.That(vitals).IsEqualTo(new HostVitals(42.5, 12 * GiB, 200 * GiB, 480 * GiB));
    }

    [Test]
    [Arguments(-1.0)]
    [Arguments(100.5)]
    [Arguments(double.NaN)]
    public async Task A_cpu_share_outside_0_to_100_is_dropped(double cpu)
    {
        HostVitals? vitals = HostVitals.Observed(cpu, 12 * GiB, 32 * GiB, null, null);

        await Assert.That(vitals!.CpuPercent).IsNull();
        await Assert.That(vitals.MemoryUsedBytes).IsEqualTo(12 * GiB);
    }

    [Test]
    [Arguments(-1L)]
    [Arguments(33L * 1024 * 1024 * 1024)]
    public async Task Memory_in_use_that_is_negative_or_above_the_hosts_total_is_dropped(long used)
    {
        HostVitals? vitals = HostVitals.Observed(10, used, 32 * GiB, null, null);

        await Assert.That(vitals!.MemoryUsedBytes).IsNull();
    }

    [Test]
    [Arguments(-1L, 480L)]
    [Arguments(500L, 480L)]
    [Arguments(10L, 0L)]
    public async Task An_impossible_disk_pair_is_dropped_as_a_pair(long free, long total)
    {
        HostVitals? vitals = HostVitals.Observed(10, null, 32 * GiB, free * GiB, total * GiB);

        await Assert.That(vitals!.DiskFreeBytes).IsNull();
        await Assert.That(vitals.DiskTotalBytes).IsNull();
    }

    [Test]
    public async Task An_older_agent_with_no_vitals_has_none()
    {
        await Assert.That(HostVitals.Observed(null, null, 32 * GiB, null, null)).IsNull();
    }
}
