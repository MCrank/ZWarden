using ZWarden.Agent.Health;

namespace ZWarden.Agent.Tests.Health;

/// <summary>
/// F16 PR-B: the bind-mount disk reader sums file sizes under a Server's data directory and reports the volume
/// capacity, degrading to null (never throwing) when the directory is absent.
/// </summary>
public class ServerDiskUsageReaderTests
{
    [Test]
    public async Task Sums_file_sizes_under_the_directory_and_reports_capacity()
    {
        using TempDirectory dir = new();
        await File.WriteAllBytesAsync(dir.File("a.bin"), new byte[100]);
        Directory.CreateDirectory(Path.Combine(dir.Path, "sub"));
        await File.WriteAllBytesAsync(Path.Combine(dir.Path, "sub", "b.bin"), new byte[50]);

        DiskUsage usage = new ServerDiskUsageReader().Read(dir.Path);

        await Assert.That(usage.UsedBytes).IsEqualTo(150);
        await Assert.That(usage.CapacityBytes).IsNotNull();
        await Assert.That(usage.CapacityBytes!.Value).IsGreaterThan(0);
    }

    [Test]
    public async Task A_missing_directory_reports_null_usage_without_throwing()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"zw-missing-{Guid.NewGuid():N}");

        DiskUsage usage = new ServerDiskUsageReader().Read(missing);

        await Assert.That(usage.UsedBytes).IsNull();
    }
}
