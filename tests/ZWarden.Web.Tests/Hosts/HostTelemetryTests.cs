using ZWarden.Application.Servers;
using ZWarden.Domain.Ids;
using ZWarden.Web.Components.Hosts;

namespace ZWarden.Web.Tests.Hosts;

/// <summary>
/// #170: the Hosts card telemetry projection, shared by the first render and the <c>/api/hosts/telemetry</c> poll. An
/// unreachable host has no telemetry; a connected host shows its latest vitals (— for a figure it didn't send), the
/// formatted memory / disk lines, and how old the report is — stale once it is a minute old.
/// </summary>
public class HostTelemetryTests
{
    private const long GiB = 1024L * 1024 * 1024;
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    private static HostCapacity Report(HostVitals? vitals, DateTimeOffset? at = null) =>
        new(AgentId.New(), 32 * GiB, 10 * GiB, 6 * GiB, 4 * GiB, 2 * GiB, at ?? Now.AddSeconds(-5), vitals);

    [Test]
    public async Task An_unreachable_host_has_no_telemetry()
    {
        await Assert.That(HostTelemetry.For(Report(new HostVitals(10, GiB, GiB, 2 * GiB)), connected: false, Now)).IsNull();
    }

    [Test]
    public async Task A_connected_host_shows_its_latest_vitals_and_formatted_lines()
    {
        HostTelemetry t = HostTelemetry.For(Report(new HostVitals(37.5, 12 * GiB, 212 * GiB, 480 * GiB)), true, Now)!;

        await Assert.That(t.CpuPercent).IsEqualTo(37.5);
        await Assert.That(t.MemoryUsedBytes).IsEqualTo(12 * GiB);
        await Assert.That(t.MemoryTotalBytes).IsEqualTo(32 * GiB);
        await Assert.That(t.MemoryText).IsEqualTo("12.0 GiB / 32.0 GiB");
        await Assert.That(t.DiskUsedBytes).IsEqualTo(268 * GiB);
        await Assert.That(t.DiskTotalBytes).IsEqualTo(480 * GiB);
        await Assert.That(t.DiskText).IsEqualTo("212.0 GiB free of 480.0 GiB");
        await Assert.That(t.Age).IsEqualTo("as of just now");
        await Assert.That(t.Stale).IsFalse();
    }

    [Test]
    public async Task A_report_a_minute_old_is_stale_but_keeps_its_values()
    {
        HostTelemetry t = HostTelemetry.For(
            Report(new HostVitals(37.5, 12 * GiB, 212 * GiB, 480 * GiB), Now.AddMinutes(-3)), true, Now)!;

        await Assert.That(t.Stale).IsTrue();
        await Assert.That(t.Age).IsEqualTo("stale · as of 3 min ago");
        await Assert.That(t.CpuPercent).IsEqualTo(37.5);
    }

    [Test]
    public async Task An_older_agent_without_vitals_shows_dashes()
    {
        HostTelemetry t = HostTelemetry.For(Report(vitals: null), true, Now)!;

        await Assert.That(t.CpuPercent).IsNull();
        await Assert.That(t.MemoryUsedBytes).IsNull();
        await Assert.That(t.DiskUsedBytes).IsNull();
        await Assert.That(t.MemoryText).IsEqualTo("—");
        await Assert.That(t.DiskText).IsEqualTo("—");
    }

    [Test]
    public async Task A_connected_host_with_no_report_yet_says_so()
    {
        HostTelemetry t = HostTelemetry.For(null, true, Now)!;

        await Assert.That(t.CpuPercent).IsNull();
        await Assert.That(t.MemoryText).IsEqualTo("—");
        await Assert.That(t.Age).IsEqualTo("no report yet");
        await Assert.That(t.Stale).IsFalse();
    }
}
