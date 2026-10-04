using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.Health;

namespace ZWarden.Agent.Tests.Health;

/// <summary>
/// #170: the host vitals behind the Hosts card meters — host-wide CPU busy % from <c>/proc/stat</c> deltas, memory in
/// use from <c>/proc/meminfo</c>, and the PZ data volume's free / total. Every figure is fail-soft: a missing source
/// reads as <c>null</c>, never an exception. The <c>/proc</c> root is a temp folder here.
/// </summary>
public sealed class HostVitalsReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "zw-vitals-" + Guid.NewGuid().ToString("N"));
    private readonly string _proc;
    private readonly string _data;

    public HostVitalsReaderTests()
    {
        _proc = Path.Combine(_root, "proc");
        _data = Path.Combine(_root, "pz-data");
        Directory.CreateDirectory(_proc);
        Directory.CreateDirectory(_data);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }

    [Test]
    public async Task The_first_read_has_no_cpu_figure_and_the_next_is_the_busy_share_since_then()
    {
        HostVitalsReader reader = Reader();
        // user nice system idle iowait irq softirq steal guest guest_nice
        WriteStat("cpu  100 0 100 700 100 0 0 0 0 0");
        HostVitals first = reader.Read();

        // +300 busy (user 200, system 100), +100 idle, +0 iowait ⇒ 300 / 400 = 75 %.
        WriteStat("cpu  300 0 200 800 100 0 0 0 0 0");
        HostVitals second = reader.Read();

        await Assert.That(first.CpuPercent).IsNull();
        await Assert.That(second.CpuPercent).IsEqualTo(75.0);
    }

    [Test]
    public async Task Iowait_counts_as_idle()
    {
        HostVitalsReader reader = Reader();
        WriteStat("cpu  0 0 0 0 0 0 0 0 0 0");
        reader.Read();
        WriteStat("cpu  50 0 0 0 150 0 0 0 0 0");

        await Assert.That(reader.Read().CpuPercent).IsEqualTo(25.0);
    }

    [Test]
    public async Task A_counter_that_went_backwards_reads_as_unknown_not_a_negative_share()
    {
        HostVitalsReader reader = Reader();
        WriteStat("cpu  500 0 500 500 0 0 0 0 0 0");
        reader.Read();
        WriteStat("cpu  10 0 10 10 0 0 0 0 0 0");

        await Assert.That(reader.Read().CpuPercent).IsNull();
    }

    [Test]
    public async Task The_core_count_is_the_number_of_per_cpu_lines()
    {
        File.WriteAllText(Path.Combine(_proc, "stat"),
            "cpu  1 0 1 1 0 0 0 0 0 0\ncpu0 1 0 0 0 0 0 0 0 0 0\ncpu1 1 0 0 0 0 0 0 0 0 0\ncpu2 1 0 0 0 0 0 0 0 0 0\ncpu3 1 0 0 0 0 0 0 0 0 0\nintr 1\n");

        await Assert.That(Reader().Read().CpuCores).IsEqualTo(4);
    }

    [Test]
    public async Task The_load_averages_come_from_loadavg()
    {
        File.WriteAllText(Path.Combine(_proc, "loadavg"), "0.12 0.20 1.75 1/234 5678\n");

        HostVitals vitals = Reader().Read();

        await Assert.That(vitals.LoadAverage1).IsEqualTo(0.12);
        await Assert.That(vitals.LoadAverage5).IsEqualTo(0.20);
        await Assert.That(vitals.LoadAverage15).IsEqualTo(1.75);
    }

    [Test]
    public async Task A_malformed_loadavg_reads_as_no_load()
    {
        File.WriteAllText(Path.Combine(_proc, "loadavg"), "busy\n");

        HostVitals vitals = Reader().Read();

        await Assert.That(vitals.LoadAverage1).IsNull();
        await Assert.That(vitals.CpuCores).IsNull();
    }

    [Test]
    public async Task Memory_in_use_is_total_minus_available()
    {
        File.WriteAllText(Path.Combine(_proc, "meminfo"),
            "MemTotal:       32000000 kB\nMemFree:         1000000 kB\nMemAvailable:   20000000 kB\nBuffers: 1 kB\n");

        HostVitals vitals = Reader().Read();

        await Assert.That(vitals.MemoryUsedBytes).IsEqualTo(12_000_000L * 1024);
    }

    [Test]
    public async Task No_proc_reads_as_no_cpu_and_no_memory()
    {
        HostVitals vitals = new HostVitalsReader(Options(_data), Path.Combine(_root, "absent")).Read();

        await Assert.That(vitals.CpuPercent).IsNull();
        await Assert.That(vitals.MemoryUsedBytes).IsNull();
    }

    [Test]
    public async Task Malformed_proc_files_read_as_unknown()
    {
        HostVitalsReader reader = Reader();
        WriteStat("cpu  not numbers");
        File.WriteAllText(Path.Combine(_proc, "meminfo"), "MemTotal: lots\n");
        reader.Read();

        HostVitals vitals = reader.Read();

        await Assert.That(vitals.CpuPercent).IsNull();
        await Assert.That(vitals.MemoryUsedBytes).IsNull();
    }

    [Test]
    public async Task The_data_volume_reports_its_free_and_total_space()
    {
        HostVitals vitals = Reader().Read();

        await Assert.That(vitals.DiskTotalBytes).IsNotNull();
        await Assert.That(vitals.DiskFreeBytes).IsNotNull();
        await Assert.That(vitals.DiskTotalBytes!.Value).IsGreaterThan(0);
        await Assert.That(vitals.DiskFreeBytes!.Value).IsLessThanOrEqualTo(vitals.DiskTotalBytes.Value);
    }

    [Test]
    public async Task A_missing_data_root_reads_as_no_disk_figures()
    {
        HostVitals vitals = new HostVitalsReader(Options(Path.Combine(_root, "nope")), _proc).Read();

        await Assert.That(vitals.DiskFreeBytes).IsNull();
        await Assert.That(vitals.DiskTotalBytes).IsNull();
    }

    private HostVitalsReader Reader() => new(Options(_data), _proc);

    private void WriteStat(string cpuLine) =>
        File.WriteAllText(Path.Combine(_proc, "stat"), cpuLine + "\ncpu0 1 2 3 4 5 6 7 8 9 10\nintr 1\n");

    private static IOptions<AgentOptions> Options(string dataRoot) =>
        Microsoft.Extensions.Options.Options.Create(new AgentOptions { DataMountRoot = dataRoot });
}
